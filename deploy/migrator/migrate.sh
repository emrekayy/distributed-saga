#!/bin/sh
# Her servisin migration'ını KENDİ veritabanı kullanıcısıyla uygular (least privilege, ADR-003).
# Şifreler ortam değişkeninden gelir; log'a yazılmaz.
set -eu

: "${POSTGRES_HOST:=postgres}"
: "${POSTGRES_PORT:=5432}"

apply() { # servis ön eki (ORDER/STOCK/PAYMENT) bundle adı
  prefix="$1"; bundle="$2"
  eval "db=\${${prefix}_DB_NAME}; user=\${${prefix}_DB_USER}; pwd=\${${prefix}_DB_PASSWORD}"
  echo "migrate: $bundle -> $db"
  "./efbundle-$bundle" --connection "Host=$POSTGRES_HOST;Port=$POSTGRES_PORT;Database=$db;Username=$user;Password=$pwd"
}

apply ORDER Ordering
apply STOCK Inventory
apply PAYMENT Payments
echo "migrate: done"
