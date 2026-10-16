# CLAUDE.md — Distributed Saga

Production-ready "Dağıtık İşlem ve Saga Pattern" altyapısı (CV portfolyosu): **Ordering**, **Inventory** ve **Payments**
servisleri; Ordering'deki MassTransit state machine ile orchestration saga, Transactional Outbox/Inbox, idempotent
consumer'lar ve compensation (stok iadesi, ödeme iadesi). Tüm iletişim RabbitMQ üzerinden; her servisin kendi Postgres DB'si var.

## Ayrıntılı dokümanlar (gerektiğinde oku)

- Mesaj akışı ve contract kuralları: `docs/messaging.md` (contract'lara/kuyruk adlarına dokunmadan önce oku)
- Saga: `docs/saga.md` (state diyagramı, geçiş matrisi, timeout/refund, concurrency; saga'yı değiştirmeden önce oku)
- Ordering servisi: `docs/ordering-service.md` (API, domain kuralları, persistence, migration/konfigürasyon)
- Kararlar ve gerekçeleri: `docs/decisions.md` (ADR; bir kararı değiştirmeden önce oku ve kullanıcıya sor)
- Bilinen tuzaklar: `docs/troubleshooting.md` (build/test/çalıştırma garip davranırsa önce buraya bak)

## Değişmez teknik kurallar (değiştirmeden önce kullanıcıya sor)

- **Framework:** hedef `net9.0`; makinede yalnızca .NET 10 SDK/runtime var (`global.json` `rollForward: latestMajor`,
  `RollForward=Major`). **.NET 9 SDK kurma.**
- **MassTransit 8.x** (8.5.11). **v9 ticari lisanslı, ASLA v9'a yükseltme.** RabbitMQ transport, EF Core outbox/inbox.
- **PostgreSQL + EF Core (Npgsql), code-first.** Database-per-service: `order_db`, `stock_db`, `payment_db`; her biri
  ayrı kullanıcıyla, yalnızca kendi DB'sine erişim (`.env.example`, `deploy/postgres/init/01-init-databases.sh`).
- **Central Package Management:** sürümler **yalnızca** `Directory.Packages.props`'ta; `.csproj`'a `Version` yazma.
- **Kalite kapısı:** `TreatWarningsAsErrors`, Nullable, `EnforceCodeStyleInBuild`; file-scoped namespace,
  private alanlar `_camelCase`, LF, BOM'suz UTF-8. Build 0 uyarı / 0 hata olmalı.
- **Katmanlar:** `X.API → X.Infrastructure → X.Application → X.Domain`. Domain hiçbir projeye bağımlı değil;
  Application'da MassTransit abstraction'ları olabilir, EF olamaz. `Shared.Contracts` bağımlılıksız;
  `BuildingBlocks.Messaging` ortak MassTransit/RabbitMQ/Outbox/retry konfigürasyonu.
- **Servisler arası senkron HTTP YOK.** Event publish (fan-out), command send (sabit kuyruk: `QueueNames`).
- **Namespace = wire contract:** MassTransit exchange adını tam tip adından üretir. Shared.Contracts namespace'leri,
  tip adları ve `QueueNames` değiştirilmez; contract'a yalnızca optional (non-required) alan eklenir.
- **Idempotency:** MessageId tekrarı MassTransit Inbox (`InboxState`); üstüne iş anahtarları (unique index:
  Payments `OrderId`, Inventory rezervasyon `OrderId`). Kendi "ProcessedMessages" tablonu yazma.
  Stok rezervasyonu ya hep ya hiç. Compensation komutları (`ReleaseStock`, `RefundPayment`) iş açısından başarısız
  olamaz: kayıt yoksa/zaten yapılmışsa da başarı event'i publish edilir.
- **Retry:** consumer/saga endpoint'lerinde `UseMessageRetry(r => r.UseTransientDatabaseRetry(<domain exception>))`
  dışta, `UseEntityFrameworkOutbox<TDbContext>` içte.
- **Zamanlayıcı:** MassTransit.Quartz + Postgres job store (`quartz` şeması). RabbitMQ delayed-exchange kullanılmaz.
- **Migration:** startup'ta yalnızca Development'ta; production'da `dotnet ef migrations bundle`. Secret'lar `.env`'de.

## Klasör yapısı

```
DistributedSaga/
├── src/
│   ├── BuildingBlocks/
│   │   ├── Shared.Contracts/          # Event & command sözleşmeleri (bağımlılıksız)
│   │   └── BuildingBlocks.Messaging/  # AddMessaging, QueueNames, CommandEndpoints, TransientDatabaseRetry, .env
│   └── Services/
│       ├── Ordering/  Ordering.Domain · Ordering.Application · Ordering.Infrastructure · Ordering.API
│       ├── Inventory/ Inventory.Domain · Inventory.Application · Inventory.Infrastructure · Inventory.API
│       └── Payments/  Payments.Domain · Payments.Application · Payments.Infrastructure · Payments.API
├── tests/            Ordering.UnitTests · Inventory.UnitTests · Payments.UnitTests (→ Domain + Application + Messaging)
│                     Shared.Contracts.Tests (sözleşme mimari testleri) · BuildingBlocks.Messaging.Tests (retry)
├── docs/                                        # Ayrıntılar (yukarıdaki listeye bak)
├── deploy/postgres/init/01-init-databases.sh   # İlk açılışta DB + kullanıcıları oluşturur
├── docker-compose.yml · .env.example (.env git'e girmez)
├── Directory.Build.props · Directory.Packages.props · global.json · .editorconfig
├── scaffold.sh                                  # Solution iskeleti (idempotent)
├── dotnet-tools.json                            # Yerel araçlar: dotnet-ef 9.0.x
└── DistributedSaga.sln
```

Proje referansları: `X.Application → X.Domain, Shared.Contracts` · `X.Infrastructure → X.Application,
BuildingBlocks.Messaging` · `X.API → X.Application, X.Infrastructure`.

## Komutlar

```bash
# İlk kurulum
cp .env.example .env
./scaffold.sh                       # idempotent: var olan projeleri atlar
dotnet tool restore                 # dotnet-ef

# Build / test (bkz. "Komut çıktısı")
dotnet build DistributedSaga.sln -v q -clp:ErrorsOnly -nologo
dotnet test DistributedSaga.sln -v q --nologo
dotnet list DistributedSaga.sln package --outdated   # MassTransit'i yalnızca 8.x içinde güncelle

# EF Core migration (Ordering)
dotnet ef migrations add <Ad> -p src/Services/Ordering/Ordering.Infrastructure -s src/Services/Ordering/Ordering.API -o Persistence/Migrations
dotnet ef migrations has-pending-model-changes -p src/Services/Ordering/Ordering.Infrastructure -s src/Services/Ordering/Ordering.API
dotnet ef migrations bundle -p src/Services/Ordering/Ordering.Infrastructure -s src/Services/Ordering/Ordering.API   # production

# Ordering API (Development; .env kökten okunur, migration startup'ta uygulanır) → http://localhost:5100
dotnet run --project src/Services/Ordering/Ordering.API
OrderSaga__PaymentTimeout=00:00:10 dotnet run --project src/Services/Ordering/Ordering.API   # kısa timeout
curl -i -X POST localhost:5100/orders -H 'Content-Type: application/json' -H 'Idempotency-Key: demo-1' \
  -d '{"customerId":"6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b","currency":"TRY","items":[{"productId":"a1a1a1a1-0000-0000-0000-000000000001","quantity":2,"unitPrice":150}]}'
curl localhost:5100/orders/<id>
curl localhost:5100/internal/orders/<id>/saga               # yalnızca Development: saga state
curl localhost:5100/health/ready                           # Postgres + RabbitMQ

# Altyapı
docker compose up -d                # postgres + rabbitmq
docker compose ps                   # ikisi de (healthy) olmalı
docker compose --profile tools up -d   # + pgAdmin (http://localhost:5050)
docker compose down                 # durdur (veri volume'larda kalır)
docker compose down -v              # volume'larla birlikte sil → init script yeniden çalışır

# Doğrulama
docker exec saga-postgres psql -U saga_admin -d postgres -c '\l'
docker exec saga-rabbitmq rabbitmqctl list_vhosts
docker exec saga-rabbitmq rabbitmqctl list_queues -p saga name messages_ready consumers
```

RabbitMQ UI: http://localhost:15672 (vhost `saga`) · PostgreSQL: `localhost:5432`.

## Komut çıktısı

- Build: `dotnet build DistributedSaga.sln -v q -clp:ErrorsOnly -nologo` (başarıda ~6 satır).
- Test: `dotnet test DistributedSaga.sln -v q --nologo` (~18 satır). Yalnızca özet için
  `| grep -E "Toplam:|Başarısız [A-Z]|error"` (proje başına bir satır).
- Yalnızca hata çıkarsa ilgili projeyi ayrıntılı çalıştır (örn. `dotnet test tests/Ordering.UnitTests --filter <Ad>`).
- Uzun log'ları (`docker logs`, API log'u, `dotnet run` çıktısı) `grep`/`tail` ile filtrele; tamamını okuma.
- Dosyaları gerektiği kadar oku (ilgili bölüm/satır aralığı); tüm solution'ı tarama, önce `docs/` ve klasör yapısına bak.

## Compact instructions

Compact sırasında koru: mevcut adımın kullanıcıyla kararlaştırılan kararları ve onaylanan plan, değiştirilen/eklenen
dosyaların listesi, henüz commit'lenmemiş işler, başarısız testlerin adı ve hata mesajı, açık sorular.
Atla: çözülmüş hataların tam çıktıları, başarılı build/test log'ları, okunmuş dosya içerikleri (yolu yeterli).

## Yol haritası ve durum

- [x] **Adım 1** — Solution iskeleti + yerel altyapı (postgres, rabbitmq)
- [x] **Adım 2** — Shared.Contracts (event/command sözleşmeleri + mimari testler)
- [x] **Adım 3** — Ordering servisi + Saga (state machine, EF saga repository, outbox, Quartz timeout, refund)
- [x] Servis adları: Order/Stock/Payment → Ordering/Inventory/Payments (contract namespace'leri değişmedi)
- [ ] **Adım 4** — Inventory/Payments consumer'ları (idempotent, outbox)
- [ ] **Adım 5** — Hata ve compensation testleri (Testcontainers, Quartz entegrasyonu, stok/ödeme iadesi)

Durum: build 0/0, 68 test yeşil (Ordering 50, Contracts 10, Messaging 8).

Adım 4 notları: consumer'lar `QueueNames` kuyruklarını dinler; `AddMessaging<TDbContext>` + `TransientDatabaseRetry`;
iş anahtarı `OrderId` unique; `RefundPayment` → tahsilat yoksa/zaten iade edildiyse de `PaymentRefunded`
(Amount = fiilen iade edilen). Ayrıntı: `docs/messaging.md`.

Bir sonraki adıma kullanıcı açıkça söylemeden geçme.
