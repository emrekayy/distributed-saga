# Inventory servisi

Stok yönetimi ve rezervasyon. `stock_db` (kullanıcı `stock_svc`), port **5200**. Katmanlar: Domain (`Product`, `Reservation`)
→ Application (consumer'lar, repository arayüzleri) → Infrastructure (EF, `FOR UPDATE`, consumer definition'ları) → API.

## Domain

- `Product(Id, Name, AvailableQuantity)`: stok negatife düşemez (domain + DB `CHECK`).
- `Reservation(OrderId PK, Status, Rejection, Reason, Items)`: `Reserved | Rejected | Released`. **Başarısız sonuçlar da
  kaydedilir**; böylece tekrar gelen komut kayıtlı sonucu yeniden yayınlar. `Reservation.Create` ya hep ya hiç çalışır:
  bir kalem bile karşılanamazsa hiçbir ürüne dokunulmaz. Aynı ürünün birden çok kalemi toplanır.
- Ret nedenleri Domain'de `RejectionReason`; contract kodu (`ReasonCodes`) Application'da eşlenir:
  `ProductNotFound → PRODUCT_NOT_FOUND`, `InsufficientStock → INSUFFICIENT_STOCK`, `ValidationError → VALIDATION_ERROR`
  (boş kalem listesi, quantity ≤ 0). Hatalı girdi exception değil kaydedilmiş bir redtir; mesaj error kuyruğuna düşmez.

## Consumer'lar

| Komut | Kuyruk | Davranış |
|---|---|---|
| `ReserveStock` | `stock-reserve-stock` | Kayıt varsa sonucu yeniden yayınla. Yoksa ürünleri `ProductId` sırasıyla `FOR UPDATE` kilitle, karar ver, kaydet, `StockReserved` / `StockReservationFailed` yayınla. |
| `ReleaseStock` | `stock-release-stock` | Rezervasyonu kilitle; `Reserved` ise ürünleri kilitle, stoğu iade et, `Released` yap. Kayıt yok / `Rejected` / `Released` ise hiçbir şey değişmez. **Her durumda `StockReleased` yayınlanır.** |

Endpoint: dışta `UseMessageRetry(UseTransientDatabaseRetry(typeof(InventoryDomainException)))`, içte
`UseEntityFrameworkOutbox<InventoryDbContext>`; outbox transaction'ı **ReadCommitted** (ADR-020). Aynı OrderId ile eşzamanlı
ikinci `ReserveStock` insert'te 23505 alır, retry'da kayıtlı sonucu bulur. Released bir rezervasyon için tekrar gelen
`ReserveStock` `StockReserved` yayınlar (asıl sonuç buydu); saga Compensating/Failed'de bunu yok sayar.

## API (Development'ta migration + seed startup'ta)

- `GET /products` · `GET /products/{id}` · `POST /products/{id}/restock` `{ "quantity": n }` (demo/admin; satırı kilitler)
- `GET /internal/reservations/{orderId}`: yalnızca Development, rezervasyon sonucu
- `GET /health/live` · `GET /health/ready` (Postgres + RabbitMQ)

Seed ürünleri (sabit Id, yalnızca eksikler eklenir): `a1a1a1a1-0000-0000-0000-00000000000{1..4}` →
Keyboard 100 · Mouse 50 · **Monitor 0** (yetersiz stok demosu) · Headset 10 (eşzamanlılık testi).

## Testler

`tests/Inventory.UnitTests`: domain (ya hep ya hiç, release idempotency, doğrulama) ve consumer testleri (MassTransit test
harness + bellek içi repository): tekrar eden komut işlemi tekrarlamaz, aynı sonucu yeniden yayınlar; Release kayıt
yokken de `StockReleased` döner. Gerçek kilit/eşzamanlılık testleri (10 stoğa 50 paralel rezervasyon, deadlock yok) `tests/IntegrationTests` içindedir: bkz. `docs/testing.md`.
