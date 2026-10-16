# Payments servisi

Cüzdan bakiyesinden tahsilat ve iade. `payment_db` (kullanıcı `payment_svc`), port **5300**. Katmanlar: Domain (`Wallet`,
`Payment`) → Application (consumer'lar, repository arayüzleri) → Infrastructure (EF, `FOR UPDATE`, consumer
definition'ları) → API. Namespace'ler `Wallets` ve `Transactions` (`Payments.Domain.Payments` kök namespace ile çakışırdı).

## Domain

- `Wallet(CustomerId PK, Currency, Balance)`: bakiye negatife düşemez (domain + DB `CHECK`).
- `Payment(Id, OrderId unique, CustomerId, Amount, Currency, Status, Failure, Reason, RefundedAmount)`:
  `Completed | Failed | Refunded`. **Başarısız sonuçlar da kaydedilir**; tekrar gelen komut yeniden tahsilat yapmaz.
- `Payment.Process`: tutar ≤ 0 ya da para birimi geçersiz/uyuşmuyor → `Failed` (`VALIDATION_ERROR`); cüzdan yok ya da bakiye
  yetersiz → `Failed` (`PAYMENT_DECLINED`); aksi halde bakiyeden düş, `Completed`. Domain'in nedeni `FailureReason`;
  contract kodu Application'da eşlenir.
- `Payment.Refund`: yalnızca `Completed` ise **kayıtlı tutarın tamamı** iade edilir, `Refunded` olur ve iade edilen tutar
  döner; diğer durumlarda 0 döner ve hiçbir şey değişmez. Komuttaki tutar yalnızca tutarsızlık uyarısı için okunur.

## Consumer'lar

| Komut | Kuyruk | Davranış |
|---|---|---|
| `ProcessPayment` | `payment-process-payment` | Kayıt varsa sonucu yeniden yayınla. Yoksa cüzdanı `FOR UPDATE` kilitle, karar ver, kaydet, `PaymentCompleted` (`PaymentId` ile) / `PaymentFailed` yayınla. |
| `RefundPayment` | `payment-refund-payment` | Ödemeyi kilitle, `Completed` ise cüzdanı kilitle ve iade et. **Her durumda `PaymentRefunded` yayınlanır** (`Amount` = fiilen iade edilen, yoksa 0). |

Kilit sırası: ödeme satırı, sonra cüzdan (Process yalnızca cüzdanı kilitler). Endpoint: dışta `UseMessageRetry(
UseTransientDatabaseRetry(typeof(PaymentsDomainException)))`, içte `UseEntityFrameworkOutbox<PaymentDbContext>`; outbox
transaction'ı **ReadCommitted** (ADR-020). Aynı OrderId ile eşzamanlı ikinci `ProcessPayment` insert'te 23505 alır
(bakiye düşüşü transaction'la birlikte geri alınır), retry'da kayıtlı sonucu bulur. Refunded bir ödeme için tekrar gelen
`ProcessPayment` `PaymentCompleted` yayınlar (asıl sonuç buydu).

Bayat komut: saga `ProcessPayment`'ı TTL = `PaymentTimeout` ile gönderir (ADR-013); Payments kapalıyken kuyrukta bekleyen
komut TTL dolunca düşer, servis açılınca işlenmez.

## API (Development'ta migration + seed startup'ta)

- `GET /wallets/{customerId}` · `POST /wallets/{customerId}/top-up` `{ "amount": n, "currency": "TRY" }` (demo; cüzdan
  yoksa oluşturur, para birimi uyuşmazsa 400)
- `GET /internal/payments/{orderId}`: yalnızca Development, ödeme sonucu
- `GET /health/live` · `GET /health/ready` (Postgres + RabbitMQ)

Seed (sabit Id, yalnızca eksikler eklenir): `6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b` zengin (1.000.000 TRY),
`b2b2b2b2-0000-0000-0000-000000000002` az bakiyeli (100 TRY).

## Testler

`tests/Payments.UnitTests`: domain (bakiye düşme/iade, yetersiz bakiye, doğrulama) ve consumer testleri (MassTransit test
harness + bellek içi repository): tekrar eden komut yeniden tahsilat yapmaz, aynı sonucu yeniden yayınlar; refund kayıt
yokken / Failed iken / tekrarında da `PaymentRefunded` döner. Hata enjeksiyonu (rastgele hata, gecikme) ve gerçek
kilit/eşzamanlılık testleri Adım 5'te.
