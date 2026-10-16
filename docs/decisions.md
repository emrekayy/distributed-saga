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
→ Servis yeniden adlandırması yalnızca iç mesajların (örn. `PaymentTimeoutExpired`) exchange adını değiştirir.

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
çalıştırır. → Saga scheduler'dan bağımsız test edilir (250 ms timeout); Quartz Adım 5 entegrasyon testinde.
