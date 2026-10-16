#!/bin/bash
# Postgres container'ı İLK KEZ (boş volume ile) başladığında bir kez çalışır.
# Her servis için: ayrı rol + ayrı veritabanı + yalnızca kendi DB'sinde yetki.
set -euo pipefail

create_service_db() {
  local db="$1" user="$2" password="$3"
  echo ">> ${db} / ${user} oluşturuluyor"

  psql -v ON_ERROR_STOP=1 \
       --username "$POSTGRES_USER" --dbname postgres \
       -v db="$db" -v usr="$user" -v pwd="$password" <<-'EOSQL'
    CREATE ROLE :"usr" WITH LOGIN PASSWORD :'pwd' NOSUPERUSER NOCREATEDB NOCREATEROLE;
    CREATE DATABASE :"db" OWNER :"usr" ENCODING 'UTF8' TEMPLATE template0;
    REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
    GRANT CONNECT, TEMPORARY ON DATABASE :"db" TO :"usr";
EOSQL

  # public şemasını servisin kullanıcısına devret (EF Core migration'ları için)
  psql -v ON_ERROR_STOP=1 \
       --username "$POSTGRES_USER" --dbname "$db" \
       -v usr="$user" <<-'EOSQL'
    ALTER SCHEMA public OWNER TO :"usr";
    REVOKE ALL ON SCHEMA public FROM PUBLIC;
EOSQL
}

create_service_db "$ORDER_DB_NAME"   "$ORDER_DB_USER"   "$ORDER_DB_PASSWORD"
create_service_db "$STOCK_DB_NAME"   "$STOCK_DB_USER"   "$STOCK_DB_PASSWORD"
create_service_db "$PAYMENT_DB_NAME" "$PAYMENT_DB_USER" "$PAYMENT_DB_PASSWORD"

echo ">> Servis veritabanları hazır."
