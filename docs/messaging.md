# Mesajlaşma: sözleşmeler ve akışlar

Orchestration saga: akışı Ordering servisindeki MassTransit state machine yönetir. Event'ler publish (fan-out),
command'lar ilgili servisin kuyruğuna send (point-to-point) edilir.

## Shared.Contracts yapısı

```
Shared.Contracts/
├── Common/    OrderItemDto · ReasonCodes
├── Orders/    Events/   OrderSubmitted · OrderCompleted · OrderFailed
├── Stock/     Commands/ ReserveStock · ReleaseStock
│              Events/   StockReserved · StockReservationFailed · StockReleased
└── Payments/  Commands/ ProcessPayment · RefundPayment
               Events/   PaymentCompleted · PaymentFailed · PaymentRefunded
```

## Mesaj akış tablosu

| Mesaj | Tip | Publisher | Consumer |
|---|---|---|---|
| `OrderSubmitted` | Event | Ordering servisi (outbox) | Order saga (başlatır) |
| `ReserveStock` | Command | Order saga | Inventory servisi |
| `StockReserved` | Event | Inventory servisi | Order saga → `ProcessPayment` |
| `StockReservationFailed` | Event | Inventory servisi | Order saga → `OrderFailed` (telafi yok) |
| `ProcessPayment` | Command | Order saga | Payments servisi |
| `PaymentCompleted` | Event | Payments servisi | Order saga → `OrderCompleted` |
| `PaymentFailed` | Event | Payments servisi | Order saga → `ReleaseStock` |
| `ReleaseStock` | Command (compensation) | Order saga | Inventory servisi |
| `StockReleased` | Event | Inventory servisi | Order saga → `OrderFailed` |
| `OrderCompleted` | Event | Order saga | Ordering servisi (sipariş durumu), dış dinleyiciler |
| `OrderFailed` | Event | Order saga | Ordering servisi (sipariş durumu), dış dinleyiciler |
| `RefundPayment` | Command (compensation) | Order saga (timeout sonrası geç `PaymentCompleted`) | Payments servisi |
| `PaymentRefunded` | Event | Payments servisi | Order saga (`RefundedAt` kaydı) |
| `PaymentTimeoutExpired` *(iç, Shared.Contracts'ta değil; adı sabit: `ordering.payment-timeout-expired`)* | Schedule | Order saga → Quartz | Order saga |

## Akışlar

- **Happy path:** `OrderSubmitted → ReserveStock → StockReserved → ProcessPayment → PaymentCompleted → OrderCompleted`
- **Stok yetersiz:** `… → ReserveStock → StockReservationFailed → OrderFailed`
- **Ödeme başarısız:** `… → ProcessPayment → PaymentFailed → ReleaseStock → StockReleased → OrderFailed`
- **Ödeme timeout:** `… → ProcessPayment (TTL) → PaymentTimeoutExpired → ReleaseStock → StockReleased → OrderFailed(PAYMENT_TIMEOUT)`
- **Timeout sonrası geç ödeme:** Compensating/Failed'de `PaymentCompleted → RefundPayment → PaymentRefunded`

## Kuyruk adları (command'lar)

Sabit, wire contract'ın parçası: `BuildingBlocks.Messaging.QueueNames` → `stock-reserve-stock`, `stock-release-stock`,
`payment-process-payment`, `payment-refund-payment`. `CommandEndpoints.Map()` command tiplerini bu kuyruklara eşler
(`EndpointConvention`); saga `Send`'i adres vermeden çağırır, Application katmanı kuyruk adlarını bilmez.
Inventory/Payments consumer endpoint'leri bu adlarla açılmalı. Saga endpoint'i: `order-state` (OrderState tip adından).

Consumer endpoint'leri (Inventory/Payments): `ConsumerDefinition.EndpointName = QueueNames.X`; dışta retry
(`UseTransientDatabaseRetry` + servisin domain exception'ı Ignore), içte `UseEntityFrameworkOutbox`. Ayrıntı:
`docs/inventory-service.md`, `docs/payments-service.md`. Hatalı girdi (boş kalem, quantity/amount ≤ 0, para birimi
uyuşmazlığı) exception değil, kaydedilmiş bir `VALIDATION_ERROR` sonucudur.

Contract dışı, kalıcı/schedule edilen mesajların URN ve exchange adı attribute ile sabittir (ADR-019):
`PaymentTimeoutExpired` → `urn:message:ordering:payment-timeout-expired` / `ordering.payment-timeout-expired`.

## Sözleşme kuralları

- Shared.Contracts **sıfır paket/proje bağımlılığı**; MassTransit tipleri (`CorrelatedBy<Guid>` vb.) yok.
  Correlation saga'da `CorrelateById(x => x.Message.OrderId)` ile.
- Her mesaj `public sealed record`, property'ler `{ get; init; }`; ilk sürüm alanları `required`.
  **Versiyonlama:** yeni alan yalnızca `required` olmadan, varsayılan değerle eklenir; alan silinmez/yeniden adlandırılmaz.
- **Namespace + tip adı wire contract'tır:** MassTransit exchange adını tam tip adından üretir
  (örn. `Shared.Contracts.Stock.Events:StockReserved`). Taşımak/yeniden adlandırmak routing'i kırar.
  (Servis adları Ordering/Inventory/Payments; contract alan adları Orders/Stock/Payments olarak kalır.)
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

## Idempotency kuralları

- **Kendi "ProcessedMessages" tablonu yazma.** `MessageId` düzeyindeki tekrarı MassTransit EF Core outbox'ıyla gelen
  **Inbox (`InboxState`)** çözer. Contract'lara ayrıca mesaj ID'si eklenmez.
- Bizim katmanımız **iş anahtarlarıdır** (saga aynı komutu yeni `MessageId` ile tekrar gönderebilir; Inbox bunu yakalamaz):
  - **Payments:** `OrderId` unique (sipariş başına tek tahsilat). Tekrar gelen `ProcessPayment` yeniden tahsilat yapmaz,
    mevcut sonucu (`PaymentCompleted`/`PaymentFailed`) yeniden publish eder.
  - **Inventory:** rezervasyon `OrderId` ile unique. Tekrar gelen `ReserveStock` mevcut sonucu yeniden publish eder.
  - Sınır: "reddedilen ödemeyi başka kartla tekrar dene" veya kısmi iade gelirse `PaymentAttemptId`/`RefundId`
    gerekir (optional alan olarak, geriye uyumlu eklenir).
- **Stok rezervasyonu ya hep ya hiç:** bir kalem bile yetersizse hiçbir kalem rezerve edilmez, `StockReservationFailed` döner.
- **Compensation komutları iş açısından başarısız olamaz** (`ReleaseStock`, `RefundPayment`); yalnızca teknik retry.
  Rezervasyon/tahsilat yoksa veya zaten serbest bırakılmış/iade edilmişse yine `StockReleased`/`PaymentRefunded`
  publish edilir (`PaymentRefunded.Amount` = fiilen iade edilen tutar), aksi halde saga takılır.
  Bu yüzden `ReleaseFailed`/`RefundFailed` event'i **yoktur**.
