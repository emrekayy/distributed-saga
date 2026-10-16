using BuildingBlocks.Messaging;
using IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace IntegrationTests;

/// <summary>
/// Transactional outbox: RabbitMQ erişilemezken sipariş yine kabul edilir ve kaydedilir; broker dönünce mesaj teslim
/// edilir ve saga ilerler. Siparişin Completed olması üç servisin de (Ordering publish, Inventory ve Payments consume)
/// broker'a yeniden bağlandığını kanıtlar.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OutboxOutageTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task Order_is_accepted_while_the_broker_is_down_and_flows_through_the_saga_after_it_returns()
    {
        var productId = await fixture.SeedProductAsync(10);
        var customerId = await fixture.SeedWalletAsync(1000);
        var reserveDeliveriesBefore = fixture.InventoryConsumers.Deliveries<ReserveStock>();
        var paymentDeliveriesBefore = fixture.PaymentsConsumers.Deliveries<Shared.Contracts.Payments.Commands.ProcessPayment>();

        await fixture.StopBrokerAsync();
        Guid orderId;
        try
        {
            // Broker yok; yine de 202 (SubmitOrderAsync Accepted'ı doğrular).
            orderId = await fixture.SubmitOrderAsync(customerId, (productId, 2, 150m));

            var order = await fixture.GetOrderAsync(orderId);
            Assert.Equal("Pending", order.Status);

            // Sipariş ve giden mesaj aynı transaction'la kaydedildi; saga henüz hiçbir şey görmedi.
            Assert.Equal(1, await fixture.CountAsync(
                fixture.OrderConnectionString, "SELECT count(*) FROM \"OutboxMessage\" WHERE \"Body\"::text LIKE '%' || @id || '%'", "id", orderId.ToString()));
            await using var orderDb = fixture.CreateOrderDb();
            Assert.False(await orderDb.OrderStates.AnyAsync(s => s.CorrelationId == orderId));
        }
        finally
        {
            await fixture.StartBrokerAsync();
        }

        // Broker döndü: OrderSubmitted teslim edilir → ReserveStock → ProcessPayment → Completed.
        await fixture.WaitForOrderAsync(orderId, "Completed", TimeSpan.FromSeconds(90));

        // Üç servis de toparlandı (açık kanıt): Ordering yayınladı, Inventory ve Payments broker'dan mesaj tüketti.
        Assert.Equal(reserveDeliveriesBefore + 1, fixture.InventoryConsumers.Deliveries<ReserveStock>());
        Assert.Equal(paymentDeliveriesBefore + 1, fixture.PaymentsConsumers.Deliveries<Shared.Contracts.Payments.Commands.ProcessPayment>());
        Assert.True((await fixture.OrderingClient.GetAsync("/health/ready")).IsSuccessStatusCode, "ordering ready");
        Assert.True((await fixture.InventoryClient.GetAsync("/health/ready")).IsSuccessStatusCode, "inventory ready");
        Assert.True((await fixture.PaymentsClient.GetAsync("/health/ready")).IsSuccessStatusCode, "payments ready");

        // Teslim edilen outbox kaydı temizlenir; stok ve para tam bir kez etkilendi.
        await Eventually.UntilAsync(
            async () => await fixture.CountAsync(
                fixture.OrderConnectionString, "SELECT count(*) FROM \"OutboxMessage\" WHERE \"Body\"::text LIKE '%' || @id || '%'", "id", orderId.ToString()) == 0,
            "delivered outbox message removed");
        await using var stockDb = fixture.CreateStockDb();
        Assert.Equal(8, await stockDb.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync());
        await using var paymentDb = fixture.CreatePaymentDb();
        Assert.Equal(700m, await paymentDb.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync());

        // Tek saga instance'ı ve tek rezervasyon (DB kanıtı). Testin kendi probe bus'ı broker yeniden başlarken
        // yeniden bağlanıyor; OrderSubmitted/StockReserved o aralıkta yayınlanırsa probe onları göremez, bu yüzden
        // burada probe'a değil DB'ye bakılır (CI'da yavaş runner'da yarış çıkarıyordu).
        await using var sagaDb = fixture.CreateOrderDb();
        Assert.Equal(1, await sagaDb.OrderStates.CountAsync(s => s.CorrelationId == orderId));
        Assert.Equal(1, await stockDb.Reservations.CountAsync(r => r.OrderId == orderId));
    }

    /// <summary>
    /// Bu sınıf diğer testlerden önce ya da sonra çalışabilir; broker yeniden başladığında test bus'ının da geri
    /// geldiğini bir tur atarak doğrular, böylece sonraki testler kararlı bir sistemle başlar.
    /// Probe'un geçici (non-durable) kuyruğu yeniden bağlanırken yayınlanan sonuç event'i kaybolabilir; bu yüzden
    /// her denemede yeni bir OrderId ile komut tekrar gönderilir ve herhangi birinin dönüşü yeterlidir.
    /// </summary>
    [Fact]
    public async Task Probe_bus_works_after_broker_restart()
    {
        const int maxAttempts = 12;
        var productId = await fixture.SeedProductAsync(maxAttempts);
        var attempts = new List<Guid>();

        await Eventually.UntilAsync(
            async () =>
            {
                if (attempts.Any(id => fixture.Probe.Of<StockReserved>(id).Count == 1))
                {
                    return true;
                }

                // Yaklaşık 5 sn'de bir yeni deneme (yoklama 50 ms'dir).
                if (attempts.Count < maxAttempts && (attempts.Count == 0 || DateTimeOffset.UtcNow - _lastSent >= TimeSpan.FromSeconds(5)))
                {
                    var orderId = Guid.NewGuid();
                    attempts.Add(orderId);
                    _lastSent = DateTimeOffset.UtcNow;
                    await fixture.Probe.SendCommandAsync(
                        new ReserveStock
                        {
                            OrderId = orderId,
                            Items = [new OrderItemDto { ProductId = productId, Quantity = 1, UnitPrice = 1 }],
                            RequestedAt = DateTimeOffset.UtcNow,
                        },
                        QueueNames.ReserveStock);
                }

                return false;
            },
            "round trip works",
            TimeSpan.FromSeconds(60));
    }

    private DateTimeOffset _lastSent;
}
