# Testler: hangi test hangi garantiyi kanıtlıyor

İki katman var:

```bash
dotnet test DistributedSaga.sln -v q --nologo --filter "Category!=Integration"    # hızlı birim testler, Docker gerekmez
dotnet test tests/IntegrationTests -v q --nologo --filter "Category=Integration"   # Testcontainers, Docker şart
```

Entegrasyon testleri `tests/IntegrationTests`: Testcontainers ile gerçek Postgres 17 (repo'nun
`deploy/postgres/init/01-init-databases.sh` script'iyle, servis başına ayrı kullanıcı) ve RabbitMQ 4.1, üç API aynı test
process'inde (`WebApplicationFactory<OrderingApiMarker|InventoryApiMarker|PaymentsApiMarker>`, ortam `Testing`).
Container'lar tek `ICollectionFixture` ile bir kez başlar. Tek collection ve sıralı çalışma zorunludur: Quartz scheduler adı
process genelinde tekildir. Saga timeout'u testlerde 6 sn.

Yöntem kuralları:

- **Sleep yok.** Tek bekleme mekanizması `Eventually.UntilAsync` (timeout'lu yoklama, açıklayıcı hata).
- **Test izolasyonu:** her test kendi yeni ProductId/CustomerId/OrderId değerlerini üretir ve yalnızca onlara bakar; tablo
  temizlenmez (daha hızlı; geç gelen bir mesaj başka testin verisine dokunamaz).
- **Gözlem noktaları:** `MessageProbe` (testin kendi bus'ı: istenen MessageId ile komut gönderir, sonuç event'lerini kaydeder),
  `ConsumeActivityObserver` (aynı anda çalışan consumer tepe değeri, teslimat sayısı) ve `DbFailureLog` (EF komut ve
  SaveChanges interceptor'ları: retry ile gizlenen 40P01/23505 hataları yalnızca burada görünür).

## Garanti → test tablosu

| Garanti | Test | Nasıl kanıtlanıyor |
|---|---|---|
| Stok negatife düşmez, aşırı satış yok | `ConcurrencyTests.Fifty_parallel_reservations_for_ten_units_reserve_exactly_ten` | 10 stoğa 50 paralel `ReserveStock` (gerçek RabbitMQ): tam 10 `Reserved`, 40 `INSUFFICIENT_STOCK`, stok 0, DB'de 10/40 rezervasyon. **Asıl garanti DB'deki `CHECK ("AvailableQuantity" >= 0)` kısıtıdır** (testte varlığı doğrulanır); arka plan yoklaması (en küçük görülen değer ≥ 0) yardımcı kanıttır. |
| Gerçekten paralel çalışıyor | `ConcurrencyTests.Consumers_are_configured_to_run_in_parallel` + yukarıdaki test | `ConcurrentMessageLimit` = 16 (kodda ve consumer definition'da doğrulanır); gözlemci aynı anda çalışan consumer tepe değerini ölçer, 2..16 aralığında olmalı. |
| Çoklu ürünlü siparişlerde deadlock yok | `ConcurrencyTests.Opposite_item_orders_across_products_do_not_deadlock` | 3 ürün, 6 farklı kalem sırası, 60 paralel sipariş: hepsi `Reserved`, stoklar tam. Retry deadlock'u gizleyebileceği için iki kanıt: EF'in gördüğü `40P01` komut hatası sayısı ve `pg_stat_database.deadlocks` (okumadan önce `pg_stat_force_next_flush()` + `pg_stat_clear_snapshot()`) değişmez. Not: Postgres istatistikleri diğer backend'ler için gecikmeli yazılır; bu yüzden sayaç tek başına yeterli kanıt değildir, `40P01` yakalama ana kanıttır. |
| Çift tahsilat yok, bakiye negatife düşmez | `ConcurrencyTests.Twenty_parallel_payments_exceeding_the_balance_never_overdraw_the_wallet` | 1000 bakiyeye 20×150: 6 `Completed`, 14 `PAYMENT_DECLINED`; bakiye + `Completed` toplamı = başlangıç bakiyesi. Asıl garanti `CHECK ("Balance" >= 0)`; yoklama yardımcı kanıt. |
| Aynı MessageId tek işlem (Inbox) | `IdempotencyTests.Reserve_stock_redelivered_…` / `Process_payment_redelivered_…` | Aynı MessageId ile iki teslim: stok/bakiye **bir kez** düşer, `InboxState`'te tek kayıt. Sonuç event'i 1–2 kez gözlenebilir (aşağıdaki "Yinelenen sonuç event'leri" notu); tahsilatta iki event aynı `PaymentId`'yi taşır. |
| Farklı MessageId, aynı OrderId: tek etki | `IdempotencyTests.Reserve_stock_with_a_new_message_id_…` / `Process_payment_with_a_new_message_id_…` | Etki tek (stok/bakiye, tek `Reservation`/`Payment` satırı); kayıtlı sonuç ikinci kez yayınlanır (aynı `PaymentId`). |
| Eşzamanlı aynı OrderId: tek tahsilat | `IdempotencyTests.Two_concurrent_process_payments_…` | Test cüzdan satırını `FOR UPDATE` ile tutar, iki komut gönderilir, `pg_stat_activity`'de 2 kilit bekleyeni görülünce commit edilir (çakışma garanti). Tek `Completed`, bakiye bir kez düşer, iki sonuç aynı `PaymentId`; kaybeden `23505` aldı (EF interceptor ile görünür) ve retry kayıtlı sonucu buldu. |
| Saga happy path | `SagaTests.Happy_path_…` | Sipariş `Completed`; stok, bakiye ve ödeme kaydı doğru. |
| Stok yetersiz: ödeme hiç denenmez | `SagaTests.Insufficient_stock_…` | `Failed` / `INSUFFICIENT_STOCK`; para ve stok değişmez, ödeme kaydı yok. |
| Ödeme reddi → stok iadesi (telafi) | `SagaTests.Payment_declined_…` | `Failed` / `PAYMENT_DECLINED`; stok iade edildi, tek `StockReleased`. |
| Timeout + geç ödeme → iade (gerçek Quartz, Postgres job store) | `SagaTests.Payment_that_completes_after_the_timeout_is_refunded` | Payments 10 sn geç yanıtlar (timeout 6 sn): sipariş `Failed` / `PAYMENT_TIMEOUT`, stok iade; geç gelen `PaymentCompleted` için `RefundPayment` → ödeme `Refunded`, bakiye başlangıç değerine döner, saga `RefundedAt` yazar. |
| Geçici hatalar retry ile atlatılır | `FaultInjectionTests.Transient_payment_failures_…` | Aynı komut 3 kez geçici hatayla (`NpgsqlException`/timeout) düşer, 4. denemede işlenir; sipariş `Completed`, tahsilat tek. |
| Kalıcı hata: mesaj kaybolmaz, sistem tutarlı kalır | `FaultInjectionTests.Permanent_payment_failure_…` | Komut `payment-process-payment_error` kuyruğuna düşer (kuyruk sayısı +1); saga timeout'a gider, stok iade edilir, sipariş `Failed`; para hiç düşmez, ödeme kaydı yok. |
| Hata enjeksiyonu Production'da kapalı | `FaultInjectionProductionTests` | Production/Staging'de `Enabled=true` seçenek doğrulamasında reddedilir (startup durur); filtre yalnızca Development/Testing'de kayıtlıdır. Altyapı gerektirmez. |
| Mesaj kaybı yok (transactional outbox) | `OutboxOutageTests.Order_is_accepted_while_the_broker_is_down_…` | RabbitMQ container'ı durdurulur (pause değil: pause TCP'yi koparmaz). `POST /orders` yine 202; sipariş ve `OutboxMessage` kaydı DB'de, saga satırı yok. Broker aynı host port'ta geri gelince sipariş `Completed` olur; Inventory ve Payments teslimat sayıları +1 (üç servis de yeniden bağlandı), üç `/health/ready` başarılı, teslim edilen outbox kaydı silinir. |
| Altyapı çalışıyor | `InfrastructureSmokeTests` | Ordering `/health/ready`; `ReserveStock` gerçek broker üzerinden Inventory'ye gidip `StockReserved` döner. |

Birim testler (`tests/*.UnitTests`, `Shared.Contracts.Tests`, `BuildingBlocks.Messaging.Tests`): domain kuralları, consumer
davranışı (MassTransit test harness), saga geçiş matrisi, contract mimari kuralları, retry politikası.

## Süre

26 entegrasyon testi, art arda 3 çalıştırma: 38,3 sn · 37,8 sn · 37,4 sn (duvar saati 39/39/37 sn; container başlatma dahil
~10 sn). En uzun testler timeout içerenlerdir: geç ödeme/refund (~10 sn), kalıcı hata + timeout (~7 sn), broker kesintisi (~10 sn).

## Bilinen gürültü ve davranışlar

- **`order-state_error` kuyruğu:** `ConcurrencyTests`, `IdempotencyTests` ve `InfrastructureSmokeTests` komutları saga'yı
  atlayıp doğrudan Inventory/Payments'a gönderir. Sonuç event'leri (`StockReserved`, `PaymentCompleted`, …) saga'ya bilinmeyen
  bir OrderId ile ulaşır ve saga kayıt bulamadığı için mesaj `order-state_error` kuyruğuna düşer. Bu **beklenen gürültüdür**
  (bilinmeyen OrderId'nin error kuyruğuna düşmesi bilinçli karardır); testler bu kuyruğa assert yapmaz.
- **`Fault<ProcessPayment>` yayınlanmaz:** kalıcı hatada EF outbox transaction'ı geri aldığı için Fault event'i de
  yayınlanmaz; hata `_error` kuyruğundan görülür, saga ise timeout ile telafi eder (ADR-013).
- **Yinelenen sonuç event'leri:** garanti "tek iş etkisi"dir, "tek event" değildir. MassTransit outbox at-least-once
  çalışır: yinelenen teslimde henüz "delivered" olmayan outbox mesajı aynı MessageId ile yeniden iletilebilir (ölçüm: 40
  denemede 1); çok nadir bir yarışta ikinci teslim Inbox yerine iş anahtarıyla (OrderId) karşılanır ve kayıtlı sonuç yeni
  MessageId ile yeniden yayınlanır (tam süitte bir kez görüldü). Her iki durumda da stok/bakiye tek kez değişir ve saga
  yinelenen sonuç event'lerini her state'te yok sayar (geçiş matrisi; Failed'de tek refund koruması). Katmanlı tasarım
  (ADR-007) tam olarak bunun içindir: Inbox ilk savunma, iş anahtarı son savunma.
- **InboxState `23505` logları:** aynı MessageId'nin eşzamanlı teslimlerinde loglarda görülen unique ihlalleri beklenen retry
  yoludur.

## Testlerin bilerek kırılması (kanıt)

Bir test, koruduğu mekanizma bozulunca kırılmıyorsa bir şey kanıtlamaz. Adım 5 sırasında iki geçici bozma yapıldı, ilgili
testin yakaladığı görüldü ve değişiklikler geri alındı (commit'lenmedi):

| Geçici değişiklik | Sonuç |
|---|---|
| `InventoryRepository.GetForUpdateAsync`'ten `FOR UPDATE` kaldırıldı | `Fifty_parallel_reservations_for_ten_units_reserve_exactly_ten` düştü: **10 yerine 50** rezervasyon başarılı (aşırı satış / lost update). |
| Kilitler `ORDER BY "Id"` yerine istek sırasında, ürün başına ayrı `FOR UPDATE` ile alındı | `Opposite_item_orders_across_products_do_not_deadlock` düştü: **56 × `40P01`** komut hatası ve `pg_stat_database.deadlocks` **+56**; sipariş akışı retry fırtınasında 30 sn içinde tamamlanamadı. |

## Sorun giderme

Entegrasyon testleri takılırsa `docs/troubleshooting.md` içindeki Docker credential helper notuna bakın.

## CI (GitHub Actions)

`.github/workflows/ci.yml`, her push ve PR'da: **build** (`dotnet build` + `dotnet format --verify-no-changes`) → paralel
**birim testler**, **entegrasyon testleri** (ubuntu runner'da Docker hazır, Testcontainers çalışır) ve **Docker image build**
(3 API + migrator, `push: false`). SDK 10 (yerelle aynı), NuGet cache `setup-dotnet`'in `cache` seçeneğiyle; `.trx` sonuçları
artifact olarak yüklenir (`unit-test-results`, `integration-test-results`). Action'lar commit SHA'sına sabitlidir.
Yerelde doğrulama: `docker run --rm -v "$PWD":/repo -w /repo rhysd/actionlint:1.7.12` ve
`dotnet format DistributedSaga.sln --verify-no-changes`. `.github/dependabot.yml`: nuget, github-actions, docker, docker-compose;
**MassTransit `>=9.0.0` yok sayılır** (lisans, ADR-002), ayrıca net9.0 ile uyumsuz 10.x paketleri ve postgres/rabbitmq major'ları.

## Yayın öncesi secret taraması

```bash
docker run --rm -v "$PWD":/repo zricethezav/gitleaks:v8.30.1 git /repo --no-banner --redact   # tüm git geçmişi
docker run --rm -v "$PWD":/repo zricethezav/gitleaks:v8.30.1 dir /repo --no-banner --redact   # çalışma ağacı
```

`.gitleaks.toml` yalnızca Testcontainers'ın geçici container parolalarını (`IntegrationFixture.cs`) allowlist'e alır; diğer her şey
varsayılan kurallarla taranır. `.env` hiçbir commit'te yoktur (`git log --all -- .env` boş) ve değerleri `.env.example`
yer tutucularıyla aynıdır (`ChangeMe_*`); gerçek bir secret `.env`'e konursa yine git'e girmez (`.gitignore`).
