using BuildingBlocks.Messaging;
using IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Events;

namespace IntegrationTests;

/// <summary>Payments'a enjekte edilen hatalar: sistem ya retry ile toparlanır ya da tutarlı şekilde telafi eder.</summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FaultInjectionTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task Transient_payment_failures_are_retried_until_the_order_completes()
    {
        var productId = await fixture.SeedProductAsync(10);
        var customerId = await fixture.SeedWalletAsync(1000);
        var injectedBefore = fixture.PaymentsFaultFilter.InjectedTransientFailures;

        Guid orderId;
        using (fixture.InjectPaymentFaults(transientFailureRate: 1.0, maxTransientFailuresPerMessage: 3))
        {
            orderId = await fixture.SubmitOrderAsync(customerId, (productId, 1, 150m));
            await fixture.WaitForOrderAsync(orderId, "Completed");
        }

        // Aynı komut 3 kez geçici hatayla düştü, 4. denemede işlendi; tahsilat tek.
        Assert.Equal(3, fixture.PaymentsFaultFilter.InjectedTransientFailures - injectedBefore);
        Assert.Single(fixture.Probe.Of<PaymentCompleted>(orderId));
        await using var db = fixture.CreatePaymentDb();
        Assert.Equal(850m, await db.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync());
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == orderId));
    }

    [Fact]
    public async Task Permanent_payment_failure_lands_in_the_error_queue_then_times_out_and_compensates()
    {
        var productId = await fixture.SeedProductAsync(10);
        var customerId = await fixture.SeedWalletAsync(1000);
        var errorQueue = QueueNames.ProcessPayment + "_error";
        var errorsBefore = await fixture.QueueMessagesAsync(errorQueue);

        Guid orderId;
        using (fixture.InjectPaymentFaults(permanentFailure: true))
        {
            orderId = await fixture.SubmitOrderAsync(customerId, (productId, 3, 100m));

            await Eventually.UntilAsync(
                async () => await fixture.QueueMessagesAsync(errorQueue) == errorsBefore + 1,
                "failed command moved to the error queue");

            // Saga hâlâ ödeme bekler; cevap gelmez → timeout → stok iadesi → Failed.
            var order = await fixture.WaitForOrderAsync(orderId, "Failed", TimeSpan.FromSeconds(60));
            Assert.Equal(ReasonCodes.PaymentTimeout, order.ReasonCode);
        }

        // Tutarlılık: stok geri geldi, para hiç düşmedi, ödeme kaydı yok, tek StockReleased.
        await using var stockDb = fixture.CreateStockDb();
        await Eventually.UntilAsync(
            async () => await stockDb.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync() == 10,
            "stock returned");
        Assert.Single(fixture.Probe.Of<StockReleased>(orderId));
        await using var paymentDb = fixture.CreatePaymentDb();
        Assert.Equal(1000m, await paymentDb.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync());
        Assert.False(await paymentDb.Payments.AnyAsync(p => p.OrderId == orderId));
    }
}
