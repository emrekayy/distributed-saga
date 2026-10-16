# CLAUDE.md — Distributed Saga

Production-ready "Dağıtık İşlem ve Saga Pattern" altyapısı (CV portfolyosu): **Ordering**, **Inventory** ve **Payments**
servisleri; Ordering'deki MassTransit state machine ile orchestration saga, Transactional Outbox/Inbox, idempotent
consumer'lar ve compensation (stok iadesi, ödeme iadesi). Tüm iletişim RabbitMQ üzerinden; her servisin kendi Postgres DB'si var.

## Ayrıntılı dokümanlar (gerektiğinde oku)

- Mesaj akışı ve contract kuralları: `docs/messaging.md` (contract'lara/kuyruk adlarına dokunmadan önce oku)
- Saga: `docs/saga.md` (state diyagramı, geçiş matrisi, timeout/refund, concurrency; saga'yı değiştirmeden önce oku)
- Ordering servisi: `docs/ordering-service.md` (API, domain kuralları, persistence, migration/konfigürasyon)
- Inventory / Payments: `docs/inventory-service.md`, `docs/payments-service.md` (domain, consumer'lar, kilit sırası, API, seed)
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
  `BuildingBlocks.Messaging` ortak MassTransit/RabbitMQ/Outbox/retry konfigürasyonu; `BuildingBlocks.Web` ortak health
  endpoint'leri + `ProblemDetailsExceptionHandlerBase` (yalnızca FrameworkReference). Domain'ler contract'a bağımlı değil
  (ret/hata nedeni Domain enum'u, `ReasonCodes` eşlemesi Application'da).
- **Servisler arası senkron HTTP YOK.** Event publish (fan-out), command send (sabit kuyruk: `QueueNames`).
- **Namespace = wire contract:** MassTransit exchange adını tam tip adından üretir. Shared.Contracts namespace'leri,
  tip adları ve `QueueNames` değiştirilmez; contract'a yalnızca optional (non-required) alan eklenir.
  Contract dışı, kalıcı/schedule edilen mesajlar `[MessageUrn]` + `[EntityName]` ile sabitlenir (ADR-019).
- **Idempotency:** MessageId tekrarı MassTransit Inbox (`InboxState`); üstüne iş anahtarları (unique index:
  Payments `OrderId`, Inventory rezervasyon `OrderId`). Kendi "ProcessedMessages" tablonu yazma.
  Stok rezervasyonu ya hep ya hiç. Compensation komutları (`ReleaseStock`, `RefundPayment`) iş açısından başarısız
  olamaz: kayıt yoksa/zaten yapılmışsa da başarı event'i publish edilir.
- **Retry:** consumer/saga endpoint'lerinde `UseMessageRetry(r => r.UseTransientDatabaseRetry(<domain exception>))`
  dışta, `UseEntityFrameworkOutbox<TDbContext>` içte.
- **Eşzamanlılık:** Ordering optimistic (xmin) + retry; Inventory/Payments pessimistic (`SELECT … FOR UPDATE`, Inventory'de
  `ProductId` sırasıyla) ve outbox **ReadCommitted** (`AddMessaging(outboxIsolationLevel:)`). RepeatableRead + `FOR UPDATE`
  yapma (40001 retry fırtınası). ADR-020. Hatalı girdi exception değil, kaydedilmiş `VALIDATION_ERROR` sonucudur.
- **Para:** tüm servislerde `numeric(19,4)`; DbContext `ConfigureConventions` içinde `UseMoneyPrecision()`, tek tek `HasPrecision` yok.
- **Zamanlayıcı:** MassTransit.Quartz + Postgres job store (`quartz` şeması). RabbitMQ delayed-exchange kullanılmaz.
- **Migration:** startup'ta yalnızca Development'ta; production'da `dotnet ef migrations bundle`. Secret'lar `.env`'de.

## Klasör yapısı

```
DistributedSaga/
├── src/
│   ├── BuildingBlocks/
│   │   ├── Shared.Contracts/          # Event & command sözleşmeleri (bağımlılıksız)
│   │   ├── BuildingBlocks.Messaging/  # AddMessaging, QueueNames, CommandEndpoints, TransientDatabaseRetry, MoneyConventions, .env
│   │   └── BuildingBlocks.Web/        # MapHealthEndpoints, ProblemDetailsExceptionHandlerBase
│   └── Services/
│       ├── Ordering/  Ordering.Domain · Ordering.Application · Ordering.Infrastructure · Ordering.API
│       ├── Inventory/ Inventory.Domain · Inventory.Application · Inventory.Infrastructure · Inventory.API
│       └── Payments/  Payments.Domain · Payments.Application · Payments.Infrastructure · Payments.API
├── tests/            IntegrationTests (Testcontainers: Postgres + RabbitMQ, 3 API in-process)
│                     Ordering.UnitTests · Inventory.UnitTests · Payments.UnitTests (→ Domain + Application + Messaging)
│                     Shared.Contracts.Tests (sözleşme mimari testleri) · BuildingBlocks.Messaging.Tests (retry)
├── docs/                                        # Ayrıntılar (yukarıdaki listeye bak)
├── scripts/e2e.sh                               # 4 uçtan uca senaryo (gerçek altyapı, 3 servis)
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
dotnet test DistributedSaga.sln -v q --nologo --filter "Category!=Integration"   # hızlı: birim testler (Docker gerekmez)
dotnet test tests/IntegrationTests -v q --nologo --filter "Category=Integration"  # Testcontainers (Docker şart), tek collection, sıralı
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

# Inventory (5200) · Payments (5300): Development'ta migration + seed startup'ta; sonuçlar için dev-only
# GET /internal/reservations/{orderId} · GET /internal/payments/{orderId}
dotnet run --project src/Services/Inventory/Inventory.API
dotnet run --project src/Services/Payments/Payments.API
./scripts/e2e.sh                    # build + 3 servis + 4 senaryo; çıkışta servisleri kapatır, hatada log tail basar

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
- [x] **Adım 4** — Inventory/Payments consumer'ları (idempotent, outbox); 4c: `scripts/e2e.sh` + README "Demo"
- [ ] **Adım 5** — Hata ve compensation testleri (Testcontainers, Quartz entegrasyonu, stok/ödeme iadesi,
  eşzamanlılık: 10 stoğa 50 paralel rezervasyon, hata enjeksiyonu)

Durum: build 0/0, 113 test yeşil (Ordering 54, Inventory 19, Payments 20, Contracts 10, Messaging 10); e2e 4/4 senaryo geçti.

Bir sonraki adıma kullanıcı açıkça söylemeden geçme.
