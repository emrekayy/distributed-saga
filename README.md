# Distributed Saga — Sipariş / Stok / Ödeme

.NET 9 · MassTransit 8 (State Machine Saga) · RabbitMQ · PostgreSQL · EF Core · Transactional Outbox

## Hızlı Başlangıç

```bash
cp .env.example .env
./scaffold.sh                 # solution + projeler (idempotent, tekrar çalıştırılabilir)
docker compose up -d          # postgres + rabbitmq
docker compose ps             # ikisi de "healthy" olmalı
```

| Bileşen | Adres |
|---|---|
| RabbitMQ UI | http://localhost:15672 (vhost: `saga`) |
| PostgreSQL | `localhost:5432` — `order_db`, `stock_db`, `payment_db` |
| pgAdmin (opsiyonel) | `docker compose --profile tools up -d` → http://localhost:5050 |

## Klasör Yapısı

```
DistributedSaga/
├── src/
│   ├── BuildingBlocks/
│   │   ├── Shared.Contracts/          # Event & command sözleşmeleri (bağımlılıksız)
│   │   └── BuildingBlocks.Messaging/  # MassTransit + RabbitMQ + Outbox ortak config
│   └── Services/
│       ├── Order/    Order.Domain · Order.Application · Order.Infrastructure · Order.API
│       ├── Stock/    Stock.Domain · Stock.Application · Stock.Infrastructure · Stock.API
│       └── Payment/  Payment.Domain · Payment.Application · Payment.Infrastructure · Payment.API
├── tests/            Order.UnitTests · Stock.UnitTests · Payment.UnitTests
├── deploy/postgres/init/01-init-databases.sh
├── docker-compose.yml · .env.example
├── Directory.Build.props · Directory.Packages.props · global.json · .editorconfig
└── DistributedSaga.sln
```

Bağımlılık yönü: `API → Infrastructure → Application → Domain`. Domain hiçbir projeye bağlı değildir.

## Mimari Kararlar

- **Database-per-Service:** Tek Postgres instance, servis başına ayrı DB ve ayrı kullanıcı; her kullanıcı yalnızca kendi DB'sine erişir. Canlıda ayrı instance'lara bölmek yalnızca connection string değişikliğidir.
- **MassTransit v8:** v9 ticari lisanslı; v8 açık kaynak (Apache 2.0) olarak kalıyor. Proje bilinçli olarak v8'de.
- **Central Package Management:** Tüm sürümler `Directory.Packages.props` içinde.
- **Kalite kapısı:** `TreatWarningsAsErrors`, nullable reference types açık.
