#!/usr/bin/env bash
# =============================================================================
#  Tek siparişin üç servisi kapsayan TEK bir trace üretip üretmediğini doğrular.
#
#  Önkoşul : docker compose --profile app --profile observability up -d --build   (stack healthy)
#            curl + jq + openssl
#  Çalıştır: ./scripts/verify-trace.sh
#
#  POST /orders'a bilinen bir W3C `traceparent` verilir; Jaeger v2 API'sinden (/api/v3, OTLP JSON) o trace çekilir ve
#  ordering, inventory, payments servislerinin span'lerinin AYNI trace'te olduğu doğrulanır.
# =============================================================================
set -euo pipefail

ORDERING=${ORDERING:-http://localhost:5100}
JAEGER=${JAEGER:-http://localhost:16686}
KEYBOARD=a1a1a1a1-0000-0000-0000-000000000001
RICH=6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b
EXPECTED="inventory ordering payments"

for tool in curl jq openssl; do
  command -v "$tool" >/dev/null || { echo "'$tool' bulunamadı" >&2; exit 1; }
done
curl -fs "$JAEGER/api/v3/services" >/dev/null || { echo "Jaeger erişilemiyor ($JAEGER); observability profili açık mı?" >&2; exit 1; }

trace_id="$(openssl rand -hex 16)"
span_id="$(openssl rand -hex 8)"

order_id="$(curl -fsS -X POST "$ORDERING/orders" \
  -H 'Content-Type: application/json' \
  -H "Idempotency-Key: trace-$trace_id" \
  -H "traceparent: 00-$trace_id-$span_id-01" \
  -d "{\"customerId\":\"$RICH\",\"currency\":\"TRY\",\"items\":[{\"productId\":\"$KEYBOARD\",\"quantity\":1,\"unitPrice\":150}]}" \
  | jq -r .id)"
echo "sipariş $order_id  trace $trace_id"

# Sipariş sonuçlanana kadar bekle (saga tamamlanınca tüm span'ler üretilmiş olur).
deadline=$((SECONDS + 30))
until [ "$(curl -fsS "$ORDERING/orders/$order_id" | jq -r .status)" = Completed ]; do
  [ "$SECONDS" -lt "$deadline" ] || { echo "sipariş 30 sn içinde Completed olmadı" >&2; exit 1; }
  sleep 0.5
done

# Jaeger'dan trace'i OTLP JSON olarak çeker; yoksa boş çıktı. Parçalı yanıtlar jq -s ile birleştirilir.
fetch_trace() {
  curl -fs "$JAEGER/api/v3/traces/$trace_id" 2>/dev/null | jq -s '[.[].result.resourceSpans[]?]' 2>/dev/null || echo '[]'
}

SERVICES_JQ='[.[].resource.attributes[] | select(.key == "service.name") | .value.stringValue] | unique | sort | join(" ")'

# Jaeger'a yazılması (batch export ~5 sn) için bekle.
services=""
deadline=$((SECONDS + 45))
while true; do
  services="$(fetch_trace | jq -r "$SERVICES_JQ")"
  [ "$services" = "$EXPECTED" ] && break
  [ "$SECONDS" -lt "$deadline" ] || break
  sleep 2
done

echo "trace'teki servisler: ${services:-<yok>}"
if [ "$services" != "$EXPECTED" ]; then
  echo "BEKLENEN: $EXPECTED" >&2
  exit 1
fi

fetch_trace | jq -r '
  [.[] | (.resource.attributes[] | select(.key == "service.name") | .value.stringValue) as $svc
       | .scopeSpans[].spans[] | {svc: $svc, name: .name, start: (.startTimeUnixNano | tonumber)}]
  | sort_by(.start)
  | "span sayısı: \(length) (veritabani spanleri haric gosterilir: \(map(select(.name | test("_db$"))) | length) adet)",
    (.[] | select(.name | test("_db$") | not) | "  \(.svc)\t\(.name)")' | head -60
echo "OK: tek trace, 3 servis -> $JAEGER/trace/$trace_id"
