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
│   │   ├── BuildingBlocks.Messaging/  # MassTransit + RabbitMQ + Outbox ortak config
│   │   └── BuildingBlocks.Web/        # Ortak health endpoint'leri + ProblemDetails tabanı
│   └── Services/
│       ├── Ordering/  Ordering.Domain · Ordering.Application · Ordering.Infrastructure · Ordering.API
│       ├── Inventory/ Inventory.Domain · Inventory.Application · Inventory.Infrastructure · Inventory.API
│       └── Payments/  Payments.Domain · Payments.Application · Payments.Infrastructure · Payments.API
├── tests/            Ordering.UnitTests · Inventory.UnitTests · Payments.UnitTests
├── scripts/e2e.sh               # uçtan uca demo senaryoları
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

## Demo

Üç servis (Ordering saga'yı yönetir, Inventory stoğu, Payments cüzdanı) RabbitMQ üzerinden konuşur; servisler arası
senkron HTTP yoktur. Development'ta her servis açılışta migration + seed çalıştırır.

| Servis | Port | Veritabanı | Not |
|---|---|---|---|
| Ordering | http://localhost:5100 | `order_db` | `POST /orders`, `GET /orders/{id}`, `GET /internal/orders/{id}/saga` |
| Inventory | http://localhost:5200 | `stock_db` | `GET /products`, `POST /products/{id}/restock`, `GET /internal/reservations/{orderId}` |
| Payments | http://localhost:5300 | `payment_db` | `GET /wallets/{customerId}`, `POST /wallets/{customerId}/top-up`, `GET /internal/payments/{orderId}` |

Her serviste `/health/live` ve `/health/ready` (Postgres + RabbitMQ) vardır; `/internal/*` yalnızca Development'ta açıktır.

**Seed verisi (sabit Id'ler):**

| Ürün | Id | Stok |
|---|---|---|
| Mechanical Keyboard | `a1a1a1a1-0000-0000-0000-000000000001` | 100 |
| Wireless Mouse | `a1a1a1a1-0000-0000-0000-000000000002` | 50 |
| 4K Monitor | `a1a1a1a1-0000-0000-0000-000000000003` | **0** (yetersiz stok demosu) |
| Headset | `a1a1a1a1-0000-0000-0000-000000000004` | 10 |

| Müşteri | Id | Bakiye |
|---|---|---|
| Zengin | `6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b` | 1.000.000 TRY |
| Az bakiyeli | `b2b2b2b2-0000-0000-0000-000000000002` | 100 TRY |

### Uçtan uca senaryolar

```bash
docker compose up -d && cp -n .env.example .env
./scripts/e2e.sh
```

Script derler, üç servisi başlatır (saga ödeme timeout'u 10 sn), dört senaryoyu çalıştırır, her birinde sipariş, rezervasyon,
ödeme, stok ve bakiye durumunu gösterip doğrular; çıkarken servisleri kapatır, hata olursa üç servisin log'unun son 30
satırını basar. Her çalıştırma yeni `Idempotency-Key` kullanır ve "önce/sonra" farkını ölçer, yani tekrarlanabilir.

| # | Senaryo | Beklenen sonuç |
|---|---|---|
| 1 | Happy path | `Completed`; stok −2, bakiye −300 |
| 2 | Stoğu 0 olan ürün | `Failed` (`INSUFFICIENT_STOCK`); rezervasyon `Rejected`, bakiye değişmedi |
| 3 | Az bakiyeli müşteri | `Failed` (`PAYMENT_DECLINED`); stok rezerve edilip geri iade edildi (`Released`) |
| 4 | Payments kapalıyken sipariş | `ProcessPayment` kuyrukta bekler → timeout → `Failed` (`PAYMENT_TIMEOUT`), stok iade; Payments açılınca bayat komut TTL ile düşmüş, ödeme kaydı yok, bakiye değişmedi |

Elle denemek için (servisler `dotnet run --project src/Services/<Ad>/<Ad>.API` ile açık):

```bash
curl -i -X POST localhost:5100/orders -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" \
  -d '{"customerId":"6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b","currency":"TRY","items":[{"productId":"a1a1a1a1-0000-0000-0000-000000000001","quantity":2,"unitPrice":150}]}'
curl localhost:5100/orders/<id>
curl localhost:5200/products
curl localhost:5300/wallets/6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b
```

Verileri başa almak için `docker compose down -v` (volume'larla birlikte siler), ardından `docker compose up -d`. Ayrıntılar:
`docs/` (messaging, saga, inventory-service, payments-service, decisions).
