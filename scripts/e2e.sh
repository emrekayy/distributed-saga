#!/usr/bin/env bash
# =============================================================================
#  Uçtan uca senaryolar (gerçek altyapı, üç servis birlikte).
#
#  Önkoşul : docker compose up -d (postgres + rabbitmq healthy), .env mevcut, curl + jq + uuidgen
#  Çalıştır: ./scripts/e2e.sh
#
#  Script üç servisi (Ordering 5100, Inventory 5200, Payments 5300) derlenmiş çıktıdan arka planda başlatır,
#  log'ları .e2e/ altına yazar ve çıkarken (başarılı ya da başarısız) hepsini kapatır. Başarısız olursa
#  üç servisin log'unun son 30 satırını yazdırır. Her çalıştırmada yeni Idempotency-Key kullanır, yani
#  tekrarlanabilir; seed verisi (stok/bakiye) önceki çalıştırmalardan etkilenmez çünkü her senaryo
#  "önce / sonra" farkını ölçer.
#
#  macOS bash 3.2 uyumludur (associative array / mapfile yok).
# =============================================================================
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$PWD"
LOG_DIR="$ROOT/.e2e"
mkdir -p "$LOG_DIR"

ORDERING=http://localhost:5100
INVENTORY=http://localhost:5200
PAYMENTS=http://localhost:5300

# Seed verisi (README "Demo" bölümü ile aynı)
KEYBOARD=a1a1a1a1-0000-0000-0000-000000000001
MONITOR_OUT_OF_STOCK=a1a1a1a1-0000-0000-0000-000000000003
RICH=6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b
POOR=b2b2b2b2-0000-0000-0000-000000000002

PAYMENT_TIMEOUT_SECONDS=10
# Saga timeout'u ile ProcessPayment TTL'i aynı süre (ADR-013); restart'tan önce bunun üstüne eklenen güvenlik payı.
EXTRA_WAIT_AFTER_TIMEOUT=5

SERVICES="Inventory Payments Ordering"

# --- Süreç yönetimi ------------------------------------------------------------
pid_file() { echo "$LOG_DIR/$1.pid"; }
log_file() { echo "$LOG_DIR/$1.log"; }

service_dir() { echo "$ROOT/src/Services/$1/$1.API"; }

service_port() {
  case "$1" in
    Ordering) echo 5100 ;;
    Inventory) echo 5200 ;;
    Payments) echo 5300 ;;
  esac
}

start_service() {
  local name="$1" port dir
  port="$(service_port "$name")"
  dir="$(service_dir "$name")"
  (
    cd "$dir"
    export ASPNETCORE_ENVIRONMENT=Development
    export ASPNETCORE_URLS="http://localhost:$port"
    export OrderSaga__PaymentTimeout="00:00:$PAYMENT_TIMEOUT_SECONDS"
    exec dotnet "bin/Debug/net9.0/$name.API.dll" >"$(log_file "$name")" 2>&1
  ) &
  echo $! >"$(pid_file "$name")"
}

stop_service() {
  local name="$1" file pid
  file="$(pid_file "$name")"
  [ -f "$file" ] || return 0
  pid="$(cat "$file")"
  if kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    # Graceful shutdown için kısa bekleme, sonra zorla.
    for _ in 1 2 3 4 5 6 7 8 9 10; do
      kill -0 "$pid" 2>/dev/null || break
      sleep 0.5
    done
    kill -9 "$pid" 2>/dev/null || true
  fi
  rm -f "$file"
}

cleanup() {
  local code=$?
  trap - EXIT
  if [ "$code" -ne 0 ]; then
    echo
    echo "!! e2e BAŞARISIZ (çıkış kodu $code). Servis log'larının son 30 satırı:"
    for name in $SERVICES; do
      echo "----- $name ($(log_file "$name")) -----"
      tail -n 30 "$(log_file "$name")" 2>/dev/null || echo "(log yok)"
    done
  fi
  for name in $SERVICES; do
    stop_service "$name"
  done
  exit "$code"
}
trap cleanup EXIT

wait_ready() {
  local name="$1" port deadline
  port="$(service_port "$name")"
  deadline=$((SECONDS + 60))
  until curl -fs "http://localhost:$port/health/ready" >/dev/null 2>&1; do
    if [ "$SECONDS" -ge "$deadline" ]; then
      fail "$name 60 sn içinde hazır olmadı"
    fi
    sleep 1
  done
}

# --- Çıktı / doğrulama yardımcıları --------------------------------------------
fail() {
  echo "  ✘ $*" >&2
  exit 1
}

ok() { echo "  ✔ $*"; }

section() {
  echo
  echo "=== $* ==="
}

assert_eq() { # etiket beklenen gerçek
  if [ "$2" = "$3" ]; then
    ok "$1: $3"
  else
    fail "$1: beklenen '$2', gerçek '$3'"
  fi
}

num_eq() { # a b → 0/1 (decimal güvenli)
  jq -n --argjson a "$1" --argjson b "$2" '$a == $b'
}

assert_num() { # etiket beklenen gerçek
  if [ "$(num_eq "$2" "$3")" = "true" ]; then
    ok "$1: $3"
  else
    fail "$1: beklenen $2, gerçek $3"
  fi
}

# --- Servis sorguları -----------------------------------------------------------
jget() { curl -fsS "$1" | jq -r "$2"; }

http_code() { curl -s -o /dev/null -w '%{http_code}' "$1"; }

stock_of() { jget "$INVENTORY/products/$1" .availableQuantity; }
balance_of() { jget "$PAYMENTS/wallets/$1" .balance; }
order_status() { jget "$ORDERING/orders/$1" .status; }
order_reason() { jget "$ORDERING/orders/$1" '.reasonCode // "-"'; }
reservation_status() { curl -fsS "$INVENTORY/internal/reservations/$1" 2>/dev/null | jq -r .status || echo "-"; }
payment_status() {
  if [ "$(http_code "$PAYMENTS/internal/payments/$1")" = "404" ]; then
    echo "yok"
  else
    jget "$PAYMENTS/internal/payments/$1" .status
  fi
}

queue_ready() { # kuyruk adı → bekleyen mesaj sayısı (rabbitmqctl anlık; management API ~5 sn gecikmeli)
  docker exec saga-rabbitmq rabbitmqctl list_queues -p saga name messages_ready 2>/dev/null \
    | awk -v q="$1" '$1 == q { print $2 }'
}

place_order() { # müşteri ürün adet → sipariş id (her çağrıda yeni Idempotency-Key)
  curl -fsS -X POST "$ORDERING/orders" \
    -H 'Content-Type: application/json' \
    -H "Idempotency-Key: e2e-$(uuidgen)" \
    -d "{\"customerId\":\"$1\",\"currency\":\"TRY\",\"items\":[{\"productId\":\"$2\",\"quantity\":$3,\"unitPrice\":150}]}" \
    | jq -r .id
}

wait_order_final() { # id zaman aşımı(sn) → Completed | Failed
  local id="$1" deadline status
  deadline=$((SECONDS + $2))
  while true; do
    status="$(order_status "$id")"
    case "$status" in
      Completed | Failed)
        echo "$status"
        return 0
        ;;
    esac
    if [ "$SECONDS" -ge "$deadline" ]; then
      fail "sipariş $id $2 sn içinde sonuçlanmadı (son durum: $status)"
    fi
    sleep 0.5
  done
}

show_state() { # sipariş id, ürün, müşteri
  local order="$1" product="$2" customer="$3" payments_up=no
  curl -fs "$PAYMENTS/health/live" >/dev/null 2>&1 && payments_up=yes
  echo "  --- durum ---"
  echo "  sipariş    : $(order_status "$order") ($(order_reason "$order"))"
  echo "  saga       : $(jget "$ORDERING/internal/orders/$order/saga" .currentState)"
  echo "  rezervasyon: $(reservation_status "$order")"
  echo "  stok       : $(stock_of "$product")"
  if [ "$payments_up" = yes ]; then
    echo "  ödeme      : $(payment_status "$order")"
    echo "  bakiye     : $(balance_of "$customer")"
  else
    echo "  ödeme      : (Payments kapalı)"
    echo "  bakiye     : (Payments kapalı)"
  fi
}

# --- Hazırlık ---------------------------------------------------------------
for tool in curl jq uuidgen docker dotnet; do
  command -v "$tool" >/dev/null || fail "'$tool' bulunamadı"
done

section "Ön kontrol"
for c in saga-postgres saga-rabbitmq; do
  health="$(docker inspect -f '{{.State.Health.Status}}' "$c" 2>/dev/null || echo yok)"
  assert_eq "$c" healthy "$health"
done
for name in $SERVICES; do
  if curl -fs "http://localhost:$(service_port "$name")/health/live" >/dev/null 2>&1; then
    fail "$name zaten çalışıyor (port $(service_port "$name")); önce kapatın"
  fi
done

section "Build ve servisleri başlat"
dotnet build DistributedSaga.sln -v q -clp:ErrorsOnly -nologo
for name in $SERVICES; do
  start_service "$name"
done
for name in $SERVICES; do
  wait_ready "$name"
  ok "$name hazır (port $(service_port "$name"))"
done

# Senaryo 1 ve 3 her çalıştırmada klavye stoğundan 2 düşürür/iade eder; seed (100) tükenmesin diye
# stok azsa admin endpoint'iyle tamamlanır.
if [ "$(stock_of $KEYBOARD)" -lt 20 ]; then
  curl -fsS -X POST "$INVENTORY/products/$KEYBOARD/restock" -H 'Content-Type: application/json' \
    -d '{"quantity":100}' >/dev/null
  ok "klavye stoğu azalmıştı, 100 eklendi"
fi

# --- Senaryo 1: happy path ------------------------------------------------------
section "Senaryo 1: Happy path → Completed; stok düştü, bakiye düştü"
stock_before="$(stock_of $KEYBOARD)"
balance_before="$(balance_of $RICH)"
order1="$(place_order $RICH $KEYBOARD 2)"
assert_eq "sipariş sonucu" Completed "$(wait_order_final "$order1" 30)"
show_state "$order1" $KEYBOARD $RICH
assert_num "stok" "$((stock_before - 2))" "$(stock_of $KEYBOARD)"
assert_num "bakiye" "$(jq -n "$balance_before - 300")" "$(balance_of $RICH)"
assert_eq "rezervasyon" Reserved "$(reservation_status "$order1")"
assert_eq "ödeme" Completed "$(payment_status "$order1")"

# --- Senaryo 2: stok yok ---------------------------------------------------------
section "Senaryo 2: Stoğu 0 olan ürün → Failed (INSUFFICIENT_STOCK); bakiye değişmedi"
balance_before="$(balance_of $RICH)"
order2="$(place_order $RICH $MONITOR_OUT_OF_STOCK 1)"
assert_eq "sipariş sonucu" Failed "$(wait_order_final "$order2" 30)"
show_state "$order2" $MONITOR_OUT_OF_STOCK $RICH
assert_eq "neden" INSUFFICIENT_STOCK "$(order_reason "$order2")"
assert_num "bakiye" "$balance_before" "$(balance_of $RICH)"
assert_eq "rezervasyon" Rejected "$(reservation_status "$order2")"
assert_eq "ödeme" yok "$(payment_status "$order2")"

# --- Senaryo 3: yetersiz bakiye ----------------------------------------------------
section "Senaryo 3: Az bakiyeli müşteri → Failed (PAYMENT_DECLINED); stok rezerve edildi, sonra iade edildi"
stock_before="$(stock_of $KEYBOARD)"
balance_before="$(balance_of $POOR)"
order3="$(place_order $POOR $KEYBOARD 2)"
assert_eq "sipariş sonucu" Failed "$(wait_order_final "$order3" 30)"
show_state "$order3" $KEYBOARD $POOR
assert_eq "neden" PAYMENT_DECLINED "$(order_reason "$order3")"
assert_eq "rezervasyon (rezerve edilip iade edildi)" Released "$(reservation_status "$order3")"
assert_num "stok (iade edildi)" "$stock_before" "$(stock_of $KEYBOARD)"
assert_num "bakiye" "$balance_before" "$(balance_of $POOR)"
assert_eq "ödeme" Failed "$(payment_status "$order3")"

# --- Senaryo 4: Payments kapalı ---------------------------------------------------
section "Senaryo 4: Payments kapalıyken sipariş → timeout → Failed (PAYMENT_TIMEOUT); stok iade edildi"
stock_before="$(stock_of $KEYBOARD)"
balance_before="$(balance_of $RICH)"
stop_service Payments
ok "Payments durduruldu"
order4="$(place_order $RICH $KEYBOARD 2)"

# ProcessPayment kuyrukta bekliyor (TTL = ${PAYMENT_TIMEOUT_SECONDS} sn)
deadline=$((SECONDS + 15))
while [ "$(queue_ready payment-process-payment)" != "1" ]; do
  [ "$SECONDS" -lt "$deadline" ] || fail "ProcessPayment kuyrukta görünmedi"
  sleep 0.5
done
ok "payment-process-payment kuyruğunda bekleyen mesaj: $(queue_ready payment-process-payment)"

assert_eq "sipariş sonucu" Failed "$(wait_order_final "$order4" 60)"
show_state "$order4" $KEYBOARD $RICH
assert_eq "neden" PAYMENT_TIMEOUT "$(order_reason "$order4")"
assert_eq "rezervasyon" Released "$(reservation_status "$order4")"
assert_num "stok (iade edildi)" "$stock_before" "$(stock_of $KEYBOARD)"

echo "  Payments'ı açmadan önce ${EXTRA_WAIT_AFTER_TIMEOUT} sn daha bekleniyor (TTL'in kesin dolması için)"
sleep "$EXTRA_WAIT_AFTER_TIMEOUT"
echo "  kuyrukta bekleyen (restart öncesi): $(queue_ready payment-process-payment)"

start_service Payments
wait_ready Payments
ok "Payments yeniden açıldı"
sleep 3 # bayat komut teslim edilseydi bu sürede işlenirdi

assert_eq "kuyruk (bayat komut TTL ile düştü)" 0 "$(queue_ready payment-process-payment)"
assert_eq "ödeme kaydı (bayat komut işlenmedi)" yok "$(payment_status "$order4")"
assert_num "bakiye (değişmedi)" "$balance_before" "$(balance_of $RICH)"
assert_eq "sipariş (hâlâ)" Failed "$(order_status "$order4")"

section "TÜM SENARYOLAR GEÇTİ"
