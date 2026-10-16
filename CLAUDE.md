# CLAUDE.md — Distributed Saga

Production-ready "Dağıtık İşlem ve Saga Pattern" altyapısı (CV portfolyosu): **Order**, **Stock** ve **Payment**
servisleri; MassTransit State Machine ile orchestration saga; Transactional Outbox; idempotent consumer'lar;
compensation (stok iadesi, ödeme iadesi).

## Teknik kararlar (değiştirmeden önce kullanıcıya sor)

- **Framework:** Hedef `net9.0` (`Directory.Build.props`). Makinede .NET 10 SDK var; `global.json`
  `rollForward: latestMajor` ile onu kullanır. **.NET 9 SDK kurma.** Makinede .NET 9 runtime da yok;
  `Directory.Build.props`'taki `RollForward=Major` sayesinde test/API'ler .NET 9 runtime varsa onu, yoksa .NET 10'u kullanır.
- **MassTransit 8.x** (şu an 8.5.11). **v9 ticari lisanslı, ASLA v9'a yükseltme.** RabbitMQ transport,
  `MassTransit.EntityFrameworkCore` ile Transactional Outbox.
- **PostgreSQL + EF Core (Npgsql), code-first.** Database-per-service: `order_db`, `stock_db`, `payment_db`; her biri
  ayrı kullanıcıyla (`order_svc`, `stock_svc`, `payment_svc`), kullanıcılar yalnızca kendi DB'sine bağlanabilir.
  Bkz. `.env.example`, `deploy/postgres/init/01-init-databases.sh`.
- **Central Package Management:** Sürümler **yalnızca** `Directory.Packages.props`'ta. `.csproj`'lara `Version` yazma.
- **Kalite kapısı (`Directory.Build.props`):** `TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings`,
  `EnforceCodeStyleInBuild`, `AnalysisLevel=latest`. `.editorconfig`: file-scoped namespace (warning → hata),
  private alanlar `_camelCase`, LF satır sonu, BOM'suz UTF-8.
- **Clean Architecture:** `X.API → X.Infrastructure → X.Application → X.Domain`. Domain hiçbir projeye bağımlı değil.
  `Shared.Contracts` bağımlılıksız (yalnızca event/command sözleşmeleri). `BuildingBlocks.Messaging` ortak
  MassTransit/RabbitMQ/Outbox konfigürasyonu.
- **Servisler arası senkron HTTP çağrısı YOK.** Tüm iletişim RabbitMQ üzerinden event/command.

## Klasör yapısı

```
DistributedSaga/
├── src/
│   ├── BuildingBlocks/
│   │   ├── Shared.Contracts/          # Event & command sözleşmeleri (bağımlılıksız)
│   │   └── BuildingBlocks.Messaging/  # MassTransit + RabbitMQ + Outbox ortak config (→ Shared.Contracts)
│   └── Services/
│       ├── Order/    Order.Domain · Order.Application · Order.Infrastructure · Order.API
│       ├── Stock/    Stock.Domain · Stock.Application · Stock.Infrastructure · Stock.API
│       └── Payment/  Payment.Domain · Payment.Application · Payment.Infrastructure · Payment.API
├── tests/            Order.UnitTests · Stock.UnitTests · Payment.UnitTests (xUnit; → Domain + Application)
│                     Shared.Contracts.Tests (sözleşme mimari testleri, reflection)
├── deploy/postgres/init/01-init-databases.sh   # İlk açılışta DB + kullanıcıları oluşturur
├── docker-compose.yml · .env.example (.env git'e girmez)
├── Directory.Build.props · Directory.Packages.props · global.json · .editorconfig
├── scaffold.sh                                  # Solution iskeleti (idempotent)
└── DistributedSaga.sln
```

Proje referansları: `X.Application → X.Domain, Shared.Contracts` · `X.Infrastructure → X.Application,
BuildingBlocks.Messaging` · `X.API → X.Application, X.Infrastructure`.

## Komutlar

```bash
# İlk kurulum
cp .env.example .env
./scaffold.sh                       # idempotent: var olan projeleri atlar, tekrar çalıştırılabilir

# Build / test (sıfır uyarı beklenir; uyarılar build'i kırar)
dotnet build DistributedSaga.sln
dotnet test DistributedSaga.sln
dotnet list DistributedSaga.sln package --outdated   # MassTransit'i yalnızca 8.x içinde güncelle

# Altyapı
docker compose up -d                # postgres + rabbitmq
docker compose ps                   # ikisi de (healthy) olmalı
docker compose --profile tools up -d   # + pgAdmin (http://localhost:5050)
docker compose down                 # durdur (veri volume'larda kalır)
docker compose down -v              # volume'larla birlikte sil → init script bir sonraki açılışta yeniden çalışır

# Doğrulama
docker exec saga-postgres psql -U saga_admin -d postgres -c '\l'
docker exec saga-rabbitmq rabbitmqctl list_vhosts
```

RabbitMQ UI: http://localhost:15672 (vhost `saga`) · PostgreSQL: `localhost:5432`.

## Mesaj sözleşmeleri (Shared.Contracts)

Orchestration saga: akışı Order servisindeki MassTransit state machine yönetir. Event'ler publish (fan-out),
command'lar ilgili servisin kuyruğuna send (point-to-point) edilir.

```
Shared.Contracts/
├── Common/    OrderItemDto · ReasonCodes
├── Orders/    Events/   OrderSubmitted · OrderCompleted · OrderFailed
├── Stock/     Commands/ ReserveStock · ReleaseStock
│              Events/   StockReserved · StockReservationFailed · StockReleased
└── Payments/  Commands/ ProcessPayment · RefundPayment
               Events/   PaymentCompleted · PaymentFailed · PaymentRefunded
```

| Mesaj | Tip | Publisher | Consumer |
|---|---|---|---|
| `OrderSubmitted` | Event | Order servisi (outbox) | Order saga (başlatır) |
| `ReserveStock` | Command | Order saga | Stock servisi |
| `StockReserved` | Event | Stock servisi | Order saga → `ProcessPayment` |
| `StockReservationFailed` | Event | Stock servisi | Order saga → `OrderFailed` (telafi yok) |
| `ProcessPayment` | Command | Order saga | Payment servisi |
| `PaymentCompleted` | Event | Payment servisi | Order saga → `OrderCompleted` |
| `PaymentFailed` | Event | Payment servisi | Order saga → `ReleaseStock` |
| `ReleaseStock` | Command (compensation) | Order saga | Stock servisi |
| `StockReleased` | Event | Stock servisi | Order saga → `OrderFailed` |
| `OrderCompleted` | Event | Order saga | Order servisi (sipariş durumu), dış dinleyiciler |
| `OrderFailed` | Event | Order saga | Order servisi (sipariş durumu), dış dinleyiciler |
| `RefundPayment` *(henüz kullanılmıyor)* | Command (compensation) | Order saga | Payment servisi |
| `PaymentRefunded` *(henüz kullanılmıyor)* | Event | Payment servisi | Order saga |

Akışlar:
- **Happy path:** `OrderSubmitted → ReserveStock → StockReserved → ProcessPayment → PaymentCompleted → OrderCompleted`
- **Stok yetersiz:** `… → ReserveStock → StockReservationFailed → OrderFailed`
- **Ödeme başarısız:** `… → ProcessPayment → PaymentFailed → ReleaseStock → StockReleased → OrderFailed`

### Sözleşme kuralları

- Shared.Contracts **sıfır paket/proje bağımlılığı**; MassTransit tipleri (`CorrelatedBy<Guid>` vb.) yok.
  Correlation saga'da `CorrelateById(x => x.Message.OrderId)` ile.
- Her mesaj `public sealed record`, property'ler `{ get; init; }`; ilk sürüm alanları `required`.
  **Versiyonlama:** yeni alan yalnızca `required` olmadan, varsayılan değerle eklenir; alan silinmez/yeniden adlandırılmaz.
- **Namespace + tip adı wire contract'tır:** MassTransit exchange adını tam tip adından üretir
  (örn. `Shared.Contracts.Stock.Events:StockReserved`). Taşımak/yeniden adlandırmak routing'i kırar.
- İsimlendirme: event'ler geçmiş zaman (`Reserved|Completed|Failed|Released|Submitted|Refunded`), command'lar emir
  kipi; `Event`/`Command` soneki yok. Namespace: `Shared.Contracts.<Orders|Stock|Payments>.<Commands|Events>`;
  bunların dışındaki public tipler yalnızca `Shared.Contracts.Common`'da.
- Her mesajda `Guid OrderId` (correlation anahtarı); event'lerde `OccurredAt`, command'larda `RequestedAt`
  (`DateTimeOffset`, publisher `DateTimeOffset.UtcNow` kullanır).
- Para: `decimal Amount` + `string Currency` (ISO 4217). Toplamı Order domain'i hesaplar, saga yeniden hesaplamaz.
- Hata event'lerinde `Reason` (insan için) + `ReasonCode` (`Common.ReasonCodes`: `INSUFFICIENT_STOCK`,
  `PRODUCT_NOT_FOUND`, `PAYMENT_DECLINED`, `PAYMENT_TIMEOUT`, `PAYMENT_ERROR`, `VALIDATION_ERROR`).
  Compensation/raporlama koda göre karar verir.
- Bu kurallar `tests/Shared.Contracts.Tests` ile korunur.

### Idempotency kuralları

- **Kendi "ProcessedMessages" tablonu yazma.** `MessageId` düzeyindeki tekrarı MassTransit EF Core outbox'ıyla gelen
  **Inbox (`InboxState`)** çözer. Contract'lara ayrıca mesaj ID'si eklenmez.
- Bizim katmanımız **iş anahtarlarıdır** (saga aynı komutu yeni `MessageId` ile tekrar gönderebilir; Inbox bunu yakalamaz):
  - **Payment:** `OrderId` unique (sipariş başına tek tahsilat). Tekrar gelen `ProcessPayment` yeniden tahsilat yapmaz,
    mevcut sonucu (`PaymentCompleted`/`PaymentFailed`) yeniden publish eder.
  - **Stock:** rezervasyon `OrderId` ile unique. Tekrar gelen `ReserveStock` mevcut sonucu yeniden publish eder.
  - Sınır: "reddedilen ödemeyi başka kartla tekrar dene" veya kısmi iade gelirse `PaymentAttemptId`/`RefundId`
    gerekir (optional alan olarak, geriye uyumlu eklenir).
- **Stok rezervasyonu ya hep ya hiç:** bir kalem bile yetersizse hiçbir kalem rezerve edilmez, `StockReservationFailed` döner.
- **Compensation komutları iş açısından başarısız olamaz** (`ReleaseStock`, `RefundPayment`); yalnızca teknik retry.
  Rezervasyon/tahsilat yoksa veya zaten serbest bırakılmış/iade edilmişse yine `StockReleased`/`PaymentRefunded`
  publish edilir, aksi halde saga takılır. Bu yüzden `ReleaseFailed`/`RefundFailed` event'i **yoktur**.

## Adım 3 açık kararları (Adım 3 başında karar verilecek, şimdi uygulanmayacak)

- **Saga kalemleri nereden alır?** `ReserveStock` için kalemler gerekli: saga state'te `Items` jsonb kolonu mu,
  yoksa saga `OrderId` ile Order tablosundan mı okuyacak?
- **Order aggregate durumunu (Completed/Failed) kim günceller?** Saga activity mi, `OrderCompleted`/`OrderFailed`
  consumer'ı mı?
- **Scheduler (ödeme timeout'u):** delayed-exchange plugin'li RabbitMQ image'ı mı (`rabbitmq:4.1` image'ında yok),
  Quartz + Postgres mi? Timeout mesajı (örn. `PaymentTimeoutExpired`) saga'nın kendine gönderdiği iç mesajdır;
  Shared.Contracts'a değil Order.Application'a konur. Timeout'ta `OrderFailed` `PAYMENT_TIMEOUT` ile publish edilir.

## Notlar / tuzaklar

- .NET 10 SDK şablonları `-f net9.0` kabul etmez; `scaffold.sh` şablonları `-f`'siz üretir, `<TargetFramework>`,
  `<Nullable>`, `<ImplicitUsings>` satırlarını csproj'dan siler (merkezi props'tan gelir) ve CRLF/BOM'u temizler.
- MSBuild XML yorumlarında `--` kullanılamaz (props dosyası yüklenemez).
- Init script yalnızca **boş** postgres volume'unda çalışır; DB/kullanıcı değişikliği için `docker compose down -v`.

## Yol haritası

- [x] **Adım 1** — Solution iskeleti + yerel altyapı (postgres, rabbitmq)
- [x] **Adım 2** — Shared.Contracts (event/command sözleşmeleri + mimari testler)
- [ ] **Adım 3** — Order servisi + Saga (MassTransit State Machine, EF Core saga repository, Outbox)
- [ ] **Adım 4** — Stock/Payment consumer'ları (idempotent, outbox)
- [ ] **Adım 5** — Hata ve compensation testleri (stok iadesi, ödeme iadesi)

Bir sonraki adıma kullanıcı açıkça söylemeden geçme.
