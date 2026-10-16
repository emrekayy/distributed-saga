# CLAUDE.md — Distributed Saga

Production-ready "Dağıtık İşlem ve Saga Pattern" altyapısı (CV portfolyosu): **Order**, **Stock** ve **Payment**
servisleri; MassTransit State Machine ile orchestration saga; Transactional Outbox; idempotent consumer'lar;
compensation (stok iadesi, ödeme iadesi).

## Teknik kararlar (değiştirmeden önce kullanıcıya sor)

- **Framework:** Hedef `net9.0` (`Directory.Build.props`). Makinede .NET 10 SDK var; `global.json`
  `rollForward: latestMajor` ile onu kullanır. **.NET 9 SDK kurma.**
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

## Notlar / tuzaklar

- .NET 10 SDK şablonları `-f net9.0` kabul etmez; `scaffold.sh` şablonları `-f`'siz üretir, `<TargetFramework>`,
  `<Nullable>`, `<ImplicitUsings>` satırlarını csproj'dan siler (merkezi props'tan gelir) ve CRLF/BOM'u temizler.
- MSBuild XML yorumlarında `--` kullanılamaz (props dosyası yüklenemez).
- Init script yalnızca **boş** postgres volume'unda çalışır; DB/kullanıcı değişikliği için `docker compose down -v`.

## Yol haritası

- [x] **Adım 1** — Solution iskeleti + yerel altyapı (postgres, rabbitmq)
- [ ] **Adım 2** — Shared.Contracts (event/command sözleşmeleri)
- [ ] **Adım 3** — Order servisi + Saga (MassTransit State Machine, EF Core saga repository, Outbox)
- [ ] **Adım 4** — Stock/Payment consumer'ları (idempotent, outbox)
- [ ] **Adım 5** — Hata ve compensation testleri (stok iadesi, ödeme iadesi)

Bir sonraki adıma kullanıcı açıkça söylemeden geçme.
