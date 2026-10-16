# Mimari kararlar (ADR)

Kısa format: **Bağlam → Karar → Sonuç.** Değiştirmeden önce kullanıcıya sorulur.

**ADR-001 — net9.0 hedefi, .NET 10 SDK.** Makinede yalnızca .NET 10 SDK/runtime var. → `net9.0` hedeflenir,
`global.json` `rollForward: latestMajor`, `Directory.Build.props` `RollForward=Major` (9 runtime varsa o, yoksa 10).
.NET 9 SDK kurulmaz. → Şablonlar `-f net9.0` kabul etmez; TFM merkezi props'tan gelir.

**ADR-002 — MassTransit 8.x.** v9 ticari lisanslı, v8 Apache 2.0. → v8'de kalınır (şu an 8.5.11), v9'a yükseltilmez.
→ Güncellemeler yalnızca 8.x yamaları.

**ADR-003 — Database-per-service, tek Postgres instance.** → `order_db`/`stock_db`/`payment_db`, her biri ayrı
kullanıcı, yalnızca kendi DB'sine CONNECT. → Canlıda ayrı instance'a bölmek yalnızca connection string değişikliği.

**ADR-004 — Central Package Management + kalite kapısı.** → Sürümler yalnızca `Directory.Packages.props`;
`TreatWarningsAsErrors`, Nullable, `ImplicitUsings`, `EnforceCodeStyleInBuild`, `AnalysisLevel=latest`; `.editorconfig`:
file-scoped namespace (warning → hata), private alanlar `_camelCase`, LF, BOM'suz UTF-8. → Uyarı build'i kırar; şablon paketleri CPM'e eklenir.

**ADR-005 — Clean Architecture, asenkron iletişim.** → `API → Infrastructure → Application → Domain`; servisler arası
senkron HTTP yok, tüm iletişim RabbitMQ event/command. → Saga orchestration Ordering servisinde.

**ADR-006 — Namespace = wire contract.** MassTransit exchange adını tam tip adından üretir. → Shared.Contracts
namespace/tip adları değiştirilmez; servis adları (Ordering/Inventory/Payments) contract alan adlarından bağımsızdır.
→ Contract dışındaki mesajların adı varsayılan olarak namespace'ten türer; kalıcı olanlar ADR-019 ile sabitlenir.

**ADR-007 — Idempotency: Inbox + iş anahtarları.** → MessageId tekrarı MassTransit `InboxState`; saga'nın yeni
MessageId ile tekrar gönderdiği komutlar için iş anahtarı unique index (Payments: OrderId, Inventory: rezervasyon OrderId).
Kendi "ProcessedMessages" tablosu yok. → Payments için OrderId yeterli; çoklu deneme/kısmi iade gelirse optional alan.

**ADR-008 — Saga kalemleri state'te (jsonb).** → Saga Order tablosundan okumaz. → Saga, OrderSubmitted ile gelen
veriyle kendi kendine yeter; tutar yeniden hesaplanmaz.

**ADR-009 — Saga + aggregate tek DbContext/transaction.** → Sipariş durumunu saga activity'leri günceller;
saga state + Order + outbox tek commit. → `OrderCompleted`/`OrderFailed` yalnızca dış dünya için.

**ADR-010 — Final state yok.** → Completed/Failed normal state; `SetCompletedWhenFinalized` kullanılmaz.
→ Denetim izi kalır, geç event'ler karşılanır; temizlik ayrı retention job'ı.

**ADR-011 — Optimistic concurrency (xmin) + retry.** → Kilit yerine çakışmada retry; retry kapsamı concurrency,
23505, geçici Npgsql; domain hataları retry'a girmez. → Ortak `TransientDatabaseRetry` tüm servislerde.

**ADR-012 — Timeout: Quartz + Postgres job store.** RabbitMQ delayed-exchange plugin'i bekleyen mesajları
replike etmez. → MassTransit.Quartz in-process, `quartz` şeması, clustering. → Şema EF migration ile kurulur.

**ADR-013 — ProcessPayment TTL + geç ödeme refund.** → TTL = PaymentTimeout (bayat komut Payments'a ulaşmaz);
timeout sonrası gelen `PaymentCompleted` için tek `RefundPayment`. → Kabul edilen risk: müşteri kısa süreli çift hareket görebilir.

**ADR-014 — Idempotency-Key + request hash.** IETF taslağı. → Aynı key + farklı body 422; hash kanonik JSON'un
SHA-256'sı, kalem sırasından bağımsız. → Eşzamanlı aynı key unique index ile çözülür.

**ADR-015 — Fiyat istemciden.** Katalog servisi yok. → Bilinçli sadeleştirme; gerçek sistemde fiyat katalogdan okunur.

**ADR-016 — Migration yalnızca Development'ta startup'ta.** → Production'da `dotnet ef migrations bundle` CI/CD adımı.
→ Çoklu instance yarışı ve şema yetkisi sorunu yok.

**ADR-017 — Secret'lar `.env`'de.** → Development'ta INI provider ile okunur, ortam değişkenleri ezer; container'da
aynı anahtarlar env var. → `appsettings.json`'da secret yok.

**ADR-018 — Saga testlerinde in-memory delayed scheduler.** Quartz `SystemTime` ile test kırılgan ve testleri seri
çalıştırır. → Saga scheduler'dan bağımsız test edilir (250 ms timeout); Quartz entegrasyon testinde (`SagaTests`, `docs/testing.md`).

**ADR-019 — Contract dışı, kalıcı/schedule edilen mesajların adı sabitlenir.** MassTransit mesaj tipini (URN) ve
exchange adını varsayılan olarak CLR namespace'inden türetir; Quartz bekleyen job'larda mesaj tipini saklar
(`qrtz_triggers.job_data` → `MessageType`). Namespace değişirse (örn. Order → Ordering yeniden adlandırması) bekleyen
timeout'lar yanlış exchange'e gider / deserialize edilemez. → Shared.Contracts dışındaki, kalıcı olarak saklanan ya da
schedule edilen her mesaj `[MessageUrn("<servis>:<kebab-ad>")]` ve `[EntityName("<servis>.<kebab-ad>")]` ile sabitlenir
(ilk örnek: `PaymentTimeoutExpired` → `urn:message:ordering:payment-timeout-expired`, exchange
`ordering.payment-timeout-expired`). → Bu tipler namespace'ten bağımsız taşınabilir; adları wire contract gibi
değiştirilmez ve testle korunur (`InternalMessageNameTests`). Shared.Contracts mesajları sabitlenmez (ADR-006).

**ADR-020 — Inventory/Payments: pessimistic kilit (`FOR UPDATE`) + ReadCommitted outbox.** Bağlam: Ordering'de saga satırı
başına tek yazar vardır ve çakışma nadirdir; bu yüzden optimistic (xmin) + retry yeterlidir (ADR-011), outbox transaction'ı
MassTransit varsayılanı RepeatableRead'dir. Inventory'de ise popüler bir ürün satırına çok sayıda sipariş aynı anda yazar;
optimistic yaklaşımda her çakışan transaction baştan denenir ve çakışma oranı yük arttıkça büyür (retry fırtınası).
→ Karar: Inventory (ürün satırları) ve Payments (wallet satırı) ilgili satırları `SELECT … FOR UPDATE` ile kilitler; çakışan
işlemler hata almak yerine sırayla bekler. Inventory birden çok ürün kilitlediği için kilitler **ProductId sırasıyla**
(`ORDER BY "Id" FOR UPDATE`) alınır; sabit kilit sırası deadlock'u önler. Release'te sıra: rezervasyon satırı, sonra ürünler.
→ Sonuç: Bu servislerde outbox transaction'ı **ReadCommitted** olmalıdır (`AddMessaging(outboxIsolationLevel: …)`).
RepeatableRead'de kilidi bekleyen transaction, kilit açılınca `40001 serialization_failure` alır; Npgsql bunu geçici hata
sayar ve retry'a girer, yani kaçındığımız retry fırtınası geri gelir. ReadCommitted'da bekleyen transaction kilit açılınca
güncel satırı okur. Varsayılanın RepeatableRead olduğu `OutboxIsolationLevelTests` ile korunur; MassTransit değiştirirse
karar yeniden değerlendirilir. Ordering'in davranışı değişmez (parametre verilmez). Stok negatife düşmez: DB'de ayrıca
`CHECK ("AvailableQuantity" >= 0)` vardır. Yarış testi (10 stoğa 50 paralel rezervasyon) Testcontainers ile `tests/IntegrationTests` içinde (`docs/testing.md`).

**ADR-021 — `BuildingBlocks.Web` ve ortak para kolonu.** Health endpoint'leri ve ProblemDetails tabanı üç API'de aynıdır;
kopyalamak yerine `BuildingBlocks.Web` (yalnızca `FrameworkReference Microsoft.AspNetCore.App`, NuGet paketi yok) vardır.
Servis, kendi exception eşlemesini `ProblemDetailsExceptionHandlerBase.MapException` ile verir. Para kolonları üç serviste
`numeric(19,4)`: her DbContext `ConfigureConventions` içinde `UseMoneyPrecision()` çağırır (`BuildingBlocks.Messaging`);
tek tek `HasPrecision` yazılmaz.

**ADR-022 — Container'lar: chiseled runtime, migration bundle init servisi, demo bayrakları.** Bağlam: tek komutla ayağa
kalkan, Production ortamında koşan bir stack gerekiyor; ADR-016 gereği startup'ta migration yapılamaz.
→ **Build:** `sdk:9.0` ile derlenir (denendi; `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` ile 0/0 geçti, bu yüzden
`sdk:10.0`'a geçilmedi). Yerel ortam ve CI SDK 10 kullanır; ikisi arasında fark çıkarsa (C# 14 / analyzer) Dockerfile'larda
build stage'i `sdk:10.0` yapılır, runtime `aspnet:9.0` kalır. **Runtime:** `aspnet:9.0-noble-chiseled-extra` (shell/curl yok,
küçük, varsayılan non-root `$APP_UID`; `-extra` ICU + tzdata içerir, Quartz ve kültür biçimlendirmesi için). Healthcheck curl
olmadan: `dotnet X.API.dll --health-probe <url>` (`BuildingBlocks.Web.HealthProbe`, yalnızca BCL). **Migration:** `deploy/migrator`
imajı üç servisin `dotnet ef migrations bundle` (self-contained) çıktısını taşır; her DB kendi kullanıcısıyla migrate edilir,
compose'da API'ler `service_completed_successfully` ile bekler. **Demo bayrakları:** `Seed:Enabled` (demo verisi) ve
`InternalEndpoints:Enabled` (`/internal/*`) ortamdan bağımsızdır, varsayılan kapalıdır; Development ve compose demosunda açıktır.
Production'da `InternalEndpoints` açılırsa startup'ta uyarı loglanır (kimlik doğrulamasız, yalnızca demo). `FaultInjection` compose'da
kapalıdır ve Production'da açılamaz. Sonuç: e2e aynı doğrulamalarla compose stack'ine karşı koşar (`./scripts/e2e.sh --compose`).
