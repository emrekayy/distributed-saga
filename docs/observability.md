# Gözlemlenebilirlik

Her API `BuildingBlocks.Observability.AddObservability(serviceName)` ile kurulur (`ordering`, `inventory`, `payments`).

```bash
docker compose --profile app --profile observability up -d --build
./scripts/verify-trace.sh        # bir sipariş ver, Jaeger'dan trace'i çek, 3 servisi doğrula
```

| Ne | Nerede |
|---|---|
| Trace (Jaeger) | http://localhost:16686 |
| Metrik + uyarı kuralları (Prometheus) | http://localhost:9090 (`/alerts`) |
| RabbitMQ kuyrukları / Move messages | http://localhost:15672 (vhost `saga`) |

## Trace

Kaynaklar: ASP.NET Core (`/health/*` hariç), Npgsql (veritabanı span'leri: `order_db`, `stock_db`, `payment_db`), MassTransit
(`ActivitySource` `MassTransit`: send / receive / process / outbox). `POST /orders` → outbox → saga → `ReserveStock`
(Inventory) → `StockReserved` → `ProcessPayment` (Payments) → `PaymentCompleted` zinciri **tek trace**'tir; MassTransit
trace context'ini mesaj header'ında ve outbox satırında taşır, bu yüzden outbox gecikmesi trace'i bölmez.
`verify-trace.sh`, `POST /orders`'a bilinen bir `traceparent` verir ve `GET /api/v3/traces/<id>` (Jaeger v2, OTLP JSON) ile
span'lerin üç servisten geldiğini doğrular (örnek koşu: 3 servis, ~250 span, çoğu veritabanı span'i).

OTLP exporter yalnızca `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` / `..._METRICS_ENDPOINT` (veya `OTEL_EXPORTER_OTLP_ENDPOINT`)
tanımlıysa eklenir. Compose'da trace `http://jaeger:4318/v1/traces`, metrik Prometheus'un OTLP alıcısına
(`/api/v1/otlp/v1/metrics`) gider. Yerelde `dotnet run` ile: `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
OTEL_EXPORTER_OTLP_TRACES_ENDPOINT=http://localhost:4318/v1/traces` (Jaeger'ın 4318 portu host'a açıktır).
Observability profili kapalıyken exporter hedefe ulaşamaz ve veriyi sessizce atar; uygulama etkilenmez.

## Metrikler

ASP.NET Core, .NET runtime, Npgsql ve MassTransit (`messaging_masstransit_consume_duration_*`, ...) OTLP ile Prometheus'a gider.
RabbitMQ kuyruk derinlikleri `rabbitmq_prometheus` plugin'inden scrape edilir:
`rabbitmq_detailed_queue_messages_ready{queue="order-state_error"}`.

## Log

Development dışında tek satır JSON; her satırın `Scopes` alanında `TraceId`/`SpanId` (ActivityTrackingOptions) ve `OrderId`
bulunur. `OrderId` scope'unu consumer/saga tarafında `OrderLogScopeFilter` (mesajdaki `Guid OrderId` özelliğinden; contract'lara
interface eklenmez), HTTP tarafında `OrderEndpoints` açar. Saga geçişleri tek satırdır:
`Order {OrderId} saga state: AwaitingStock -> AwaitingPayment` (`TransitionLogObserver`).

```bash
docker logs saga-ordering-api 2>&1 | grep "saga state" | jq -r .Message
docker logs saga-inventory-api 2>&1 | jq -c 'select(.Scopes[]?.OrderId == "<orderId>") | .Message'
```

## `_error` kuyrukları

MassTransit, retry'ları tükenen mesajı `<kuyruk>_error` kuyruğuna (hata header'larıyla) taşır; tüketilmeyen tipler
`<kuyruk>_skipped`'e gider. **Otomatik redrive yoktur** (bilinçli): zehirli bir mesajı körlemesine geri sürmek aynı hatayı
döngüye sokar. Önce nedeni bulun (`MT-Fault-Message`, `MT-Fault-StackTrace` header'ları ve servis log'u), düzeltin, sonra taşıyın.

İzleme: `ErrorQueueNotEmpty` uyarısı (`deploy/prometheus/alerts.yml`) bir `_error` kuyruğunda 1 dk'dan uzun süre mesaj kalırsa
çalışır. Anlık bakış: `docker exec saga-rabbitmq rabbitmqctl list_queues -p saga name messages_ready | grep -E "_error|_skipped"`.

Geri taşıma (RabbitMQ'da `rabbitmq_shovel` açıktır, `deploy/rabbitmq/enabled_plugins`):

- **UI:** http://localhost:15672 → Queues → `order-state_error` → *Move messages* → hedef kuyruk `order-state`.
- **CLI** (tek seferlik shovel; kuyruk boşalınca kendini siler). `rabbitmqctl` parametreyi (parola dahil) çıktıya yazar;
  yalnızca yerel demoda kabul edilebilir:

```bash
docker exec saga-rabbitmq sh -c 'rabbitmqctl set_parameter -p saga shovel redrive-order-state "{\"src-protocol\":\"amqp091\",\"src-uri\":\"amqp://$RABBITMQ_DEFAULT_USER:$RABBITMQ_DEFAULT_PASS@localhost/saga\",\"src-queue\":\"order-state_error\",\"dest-protocol\":\"amqp091\",\"dest-uri\":\"amqp://$RABBITMQ_DEFAULT_USER:$RABBITMQ_DEFAULT_PASS@localhost/saga\",\"dest-queue\":\"order-state\",\"src-delete-after\":\"queue-length\"}"'
```

Taşıma idempotenttir: mesajın `MessageId`'si korunur; aynı mesaj daha önce işlendiyse Inbox (ADR-007) tekrarı yutar.
Doğrulandı: Adım 6b sırasında saga'da oluşan bir hata (`OrderSubmitted` `order-state_error`'a düştü) düzeltildikten sonra bu
shovel ile geri taşındı ve sipariş `Completed` oldu.
