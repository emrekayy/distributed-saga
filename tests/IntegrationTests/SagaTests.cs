using BuildingBlocks.Messaging;
using IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Payments.Domain.Transactions;
using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Events;

namespace IntegrationTests;

/// <summary>Üç servis in-process, gerçek RabbitMQ + Postgres + Quartz job store: saga uçtan uca.</summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SagaTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task Happy_path_reserves_stock_charges_the_wallet_and_completes_the_order()
    {
        var productId = await fixture.SeedProductAsync(10);
        var customerId = await fixture.SeedWalletAsync(1000);

        var orderId = await fixture.SubmitOrderAsync(customerId, (productId, 2, 150m));
        var order = await fixture.WaitForOrderAsync(orderId, "Completed");

        Assert.Equal(300m, order.TotalAmount);
        Assert.Equal(8, await StockAsync(productId));
        Assert.Equal(700m, await BalanceAsync(customerId));
        Assert.Equal(PaymentStatus.Completed, await PaymentStatusAsync(orderId));
        await Eventually.UntilAsync(() => fixture.Probe.Of<OrderCompleted>(orderId).Count >= 1, "OrderCompleted published");
    }

    [Fact]
    public async Task Insufficient_stock_fails_the_order_without_touching_payments()
    {
        var productId = await fixture.SeedProductAsync(1);
        var customerId = await fixture.SeedWalletAsync(1000);

        var orderId = await fixture.SubmitOrderAsync(customerId, (productId, 2, 150m));
        var order = await fixture.WaitForOrderAsync(orderId, "Failed");

        Assert.Equal(ReasonCodes.InsufficientStock, order.ReasonCode);
        Assert.Equal(1, await StockAsync(productId));
        Assert.Equal(1000m, await BalanceAsync(customerId));
        Assert.Null(await PaymentStatusAsync(orderId));
        await Eventually.UntilAsync(() => fixture.Probe.Of<OrderFailed>(orderId).Count >= 1, "OrderFailed published");
    }

    [Fact]
    public async Task Payment_declined_releases_the_reserved_stock_and_fails_the_order()
    {
        var productId = await fixture.SeedProductAsync(10);
        var customerId = await fixture.SeedWalletAsync(100);

        var orderId = await fixture.SubmitOrderAsync(customerId, (productId, 2, 150m));
        var order = await fixture.WaitForOrderAsync(orderId, "Failed");

        Assert.Equal(ReasonCodes.PaymentDeclined, order.ReasonCode);
        Assert.Equal(10, await StockAsync(productId));
        Assert.Equal(100m, await BalanceAsync(customerId));
        Assert.Equal(PaymentStatus.Failed, await PaymentStatusAsync(orderId));
        Assert.Single(fixture.Probe.Of<StockReleased>(orderId));
    }

    /// <summary>
    /// Timeout (gerçek Quartz + Postgres job store, kısa süre) ve geç ödeme: Payments timeout'tan sonra cevap verir;
    /// saga önce stoğu iade edip siparişi Failed yapar, sonra gelen PaymentCompleted'a RefundPayment ile karşılık verir.
    /// </summary>
    [Fact]
    public async Task Payment_that_completes_after_the_timeout_is_refunded()
    {
        var productId = await fixture.SeedProductAsync(10);
        var customerId = await fixture.SeedWalletAsync(1000);

        Guid orderId;
        using (fixture.InjectPaymentFaults(delay: IntegrationFixture.PaymentTimeout + TimeSpan.FromSeconds(4)))
        {
            orderId = await fixture.SubmitOrderAsync(customerId, (productId, 2, 150m));

            var order = await fixture.WaitForOrderAsync(orderId, "Failed", TimeSpan.FromSeconds(60));
            Assert.Equal(ReasonCodes.PaymentTimeout, order.ReasonCode);
            await Eventually.UntilAsync(async () => await StockAsync(productId) == 10, "stock returned after the timeout");
            Assert.Null(await PaymentStatusAsync(orderId));
        }

        // Geç ödeme tahsil edilir → saga RefundPayment gönderir → bakiye geri gelir.
        await Eventually.UntilAsync(
            async () => await PaymentStatusAsync(orderId) == PaymentStatus.Refunded,
            "late payment refunded",
            TimeSpan.FromSeconds(60));

        Assert.Equal(1000m, await BalanceAsync(customerId));
        var refunded = await Eventually.ValueAsync(
            () => Task.FromResult(fixture.Probe.Of<PaymentRefunded>(orderId).FirstOrDefault()), "PaymentRefunded published");
        Assert.Equal(300m, refunded.Amount);
        Assert.Equal(10, await StockAsync(productId));
        Assert.Equal("Failed", (await fixture.GetOrderAsync(orderId)).Status);

        await using var orderDb = fixture.CreateOrderDb();
        var saga = await orderDb.OrderStates.AsNoTracking().SingleAsync(s => s.CorrelationId == orderId);
        Assert.NotNull(saga.RefundRequestedAt);
        await Eventually.UntilAsync(
            async () => (await orderDb.OrderStates.AsNoTracking().SingleAsync(s => s.CorrelationId == orderId)).RefundedAt is not null,
            "saga recorded the refund");
    }

    private async Task<int> StockAsync(Guid productId)
    {
        await using var db = fixture.CreateStockDb();
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync();
    }

    private async Task<decimal> BalanceAsync(Guid customerId)
    {
        await using var db = fixture.CreatePaymentDb();
        return await db.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync();
    }

    private async Task<PaymentStatus?> PaymentStatusAsync(Guid orderId)
    {
        await using var db = fixture.CreatePaymentDb();
        return await db.Payments.AsNoTracking().Where(p => p.OrderId == orderId).Select(p => (PaymentStatus?)p.Status).SingleOrDefaultAsync();
    }
}
