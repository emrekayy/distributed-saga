# Ordering servisi

## Katmanlar

```
Ordering.Domain/Orders/          Order (aggregate) · OrderItem · OrderStatus · domain exception'ları
Ordering.Application/Orders/     SubmitOrderHandler · RequestHasher · IOrderRepository/IUnitOfWork · IOrderSagaStateReader
Ordering.Application/Saga/       OrderStateMachine · OrderState · activity'ler · PaymentTimeoutExpired · OrderSagaOptions
Ordering.Infrastructure/         OrderDbContext · EF configuration'ları · Migrations · Sql/quartz_tables_postgres.sql
                                 Saga/OrderStateDefinition (retry + outbox) · DependencyInjection (MassTransit, Quartz)
Ordering.API/                    Minimal API (Endpoints/) · ProblemDetails eşlemesi · composition root
```

Application'da MassTransit abstraction'ları olabilir, EF olamaz (repository arayüzü Application'da, implementasyonu
Infrastructure'da). BuildingBlocks.Messaging: `AddMessaging<TDbContext>` (RabbitMQ + EF outbox + bus outbox),
`QueueNames` + `CommandEndpoints.Map()`, `TransientDatabaseRetry`, `.env`/connection string yardımcıları.

## API (http://localhost:5100)

| Uç | Davranış |
|---|---|
| `POST /orders` | `Idempotency-Key` header zorunlu. İlk istek **202 Accepted** + `Location`; aynı key + aynı body **200** (mevcut sipariş); aynı key + farklı body **422**. Validation **400** (ProblemDetails). |
| `GET /orders/{id}` | `status` (Pending/Completed/Failed), `reasonCode`, `reason`, tutar, kalemler. Yoksa 404. |
| `GET /internal/orders/{id}/saga` | **Yalnızca Development.** Saga'nın iç state'i; public API sözleşmesinin parçası değil. |
| `/health/live` · `/health/ready` | live: bağımlılık kontrolü yok · ready: Postgres (`AddDbContextCheck`) + MassTransit bus |

- **Idempotency-Key (IETF taslağı davranışı):** `Orders.IdempotencyKey` unique index. Siparişte `RequestHash` =
  kanonik JSON'un SHA-256'sı (alan sırası sabit, kalemler ProductId'ye göre sıralı, decimal sondaki sıfırlar atılmış;
  key hash'e dahil değil). Eşzamanlı aynı key: kaybeden istek 23505 alır, değişiklikleri atılır, kazananın siparişi döner.
- `OrderSubmitted` bus outbox ile siparişle **aynı SaveChanges**'te `OutboxMessage`'a yazılır; broker'a delivery service taşır.
- **Fiyat istemciden geliyor: bilinçli sadeleştirme, gerçek sistemde fiyat katalogdan okunur.**
- Exception → ProblemDetails: `OrderValidationException` 400, `IdempotencyKeyConflictException` 422,
  `InvalidOrderStateTransitionException` 409, `BadHttpRequestException` (bozuk JSON) 400.

## Domain kuralları

En az bir kalem, adet > 0, fiyat > 0, aynı ürün tek kalemde, para birimi `^[A-Z]{3}$`, Idempotency-Key ≤ 100 karakter;
`TotalAmount` domain'de hesaplanır. `Complete()`/`Fail()` yalnızca Pending'den; aynı duruma tekrar geçiş no-op,
çapraz geçiş (Completed → Failed vb.) `InvalidOrderStateTransitionException` (API'de 409).

## Persistence

Tablolar (`order_db`, public şema): `Orders`, `OrderItems` (owned, PK OrderId+ProductId), `OrderState` (Items jsonb),
`InboxState`, `OutboxMessage`, `OutboxState`; `quartz` şeması (Quartz job store). Para `numeric(19,4)`.
Concurrency: `xmin` (bkz. docs/saga.md).

## Migration ve konfigürasyon

- **Development:** API startup'ta `Database.Migrate()` çalıştırır. **Production'da startup'ta migrate edilmez**
  (çoklu instance yarışı, şema yetkisi): `dotnet ef migrations bundle` ile üretilen bundle CI/CD'de ayrı adım olarak çalışır.
- Secret'lar repoda değil. Development'ta API kökteki `.env`'yi INI provider ile okur (anahtarlar `.env` ile aynı:
  `ORDER_DB_USER`, `RABBITMQ_PASSWORD` …); ortam değişkenleri `.env`'yi ezer. Container'da aynı anahtarlar ortam
  değişkeni olarak verilir. `appsettings.json` yalnızca secret olmayan varsayılanları içerir
  (`POSTGRES_HOST`, `RABBITMQ_VHOST`, `OrderSaga:PaymentTimeout` …).

## Testler (Ordering.UnitTests)

Domain (geçişler, invariant'lar, toplam), `RequestHasher` (sıra bağımsızlığı, decimal gösterimi), `SubmitOrderHandler`
(yeni / tekrar / 422 / eşzamanlı key yarışı; fake repository + harness `IPublishEndpoint`). Saga testleri: docs/saga.md.
