using BuildingBlocks.Messaging;
using IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payments.Domain.Transactions;
using Shared.Contracts.Common;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace IntegrationTests;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IdempotencyTests(IntegrationFixture fixture)
{
    // --- a) Aynı MessageId: MassTransit Inbox ikinci teslimi yakalar ---------------------------------------------

    [Fact]
    public async Task Reserve_stock_redelivered_with_the_same_message_id_is_processed_once()
    {
        var productId = await fixture.SeedProductAsync(10);
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var command = Reserve(orderId, productId, 3);

        await fixture.Probe.SendCommandAsync(command, QueueNames.ReserveStock, messageId);
        await fixture.Probe.SendCommandAsync(command, QueueNames.ReserveStock, messageId);

        await Eventually.UntilAsync(
            async () => fixture.Probe.Of<StockReserved>(orderId).Count >= 1 && await fixture.QueueMessagesAsync(QueueNames.ReserveStock) == 0,
            "first delivery answered and both deliveries consumed from the queue");

        AssertSingleDistinctMessage<StockReserved>(orderId);
        Assert.Equal(1, await fixture.InboxCountAsync(fixture.StockConnectionString, messageId));
        await using var db = fixture.CreateStockDb();
        Assert.Equal(7, await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync());
    }

    [Fact]
    public async Task Process_payment_redelivered_with_the_same_message_id_is_charged_once()
    {
        var customerId = await fixture.SeedWalletAsync(500);
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var command = Pay(orderId, customerId, 120);

        await fixture.Probe.SendCommandAsync(command, QueueNames.ProcessPayment, messageId);
        await fixture.Probe.SendCommandAsync(command, QueueNames.ProcessPayment, messageId);

        await Eventually.UntilAsync(
            async () => fixture.Probe.Of<PaymentCompleted>(orderId).Count >= 1 && await fixture.QueueMessagesAsync(QueueNames.ProcessPayment) == 0,
            "first delivery answered and both deliveries consumed from the queue");

        AssertSingleDistinctMessage<PaymentCompleted>(orderId);
        Assert.Equal(1, await fixture.InboxCountAsync(fixture.PaymentConnectionString, messageId));
        Assert.Equal(380m, await BalanceAsync(customerId));
    }

    // --- b) Farklı MessageId, aynı OrderId: iş anahtarı tek etki, kayıtlı sonuç yeniden yayınlanır ---------------

    [Fact]
    public async Task Reserve_stock_with_a_new_message_id_but_same_order_reserves_once_and_republishes_the_result()
    {
        var productId = await fixture.SeedProductAsync(10);
        var orderId = Guid.NewGuid();
        var command = Reserve(orderId, productId, 4);

        await fixture.Probe.SendCommandAsync(command, QueueNames.ReserveStock, Guid.NewGuid());
        await Eventually.UntilAsync(() => fixture.Probe.Of<StockReserved>(orderId).Count == 1, "first StockReserved");
        await fixture.Probe.SendCommandAsync(command, QueueNames.ReserveStock, Guid.NewGuid());
        await Eventually.UntilAsync(() => fixture.Probe.Of<StockReserved>(orderId).Count == 2, "recorded result republished");

        var results = fixture.Probe.Of<StockReserved>(orderId);
        Assert.All(results, r => Assert.Equal(orderId, r.OrderId));
        await using var db = fixture.CreateStockDb();
        Assert.Equal(6, await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync());
        Assert.Equal(1, await db.Reservations.CountAsync(r => r.OrderId == orderId));
    }

    [Fact]
    public async Task Process_payment_with_a_new_message_id_but_same_order_charges_once_and_republishes_the_result()
    {
        var customerId = await fixture.SeedWalletAsync(500);
        var orderId = Guid.NewGuid();
        var command = Pay(orderId, customerId, 200);

        await fixture.Probe.SendCommandAsync(command, QueueNames.ProcessPayment, Guid.NewGuid());
        await Eventually.UntilAsync(() => fixture.Probe.Of<PaymentCompleted>(orderId).Count == 1, "first PaymentCompleted");
        await fixture.Probe.SendCommandAsync(command, QueueNames.ProcessPayment, Guid.NewGuid());
        await Eventually.UntilAsync(() => fixture.Probe.Of<PaymentCompleted>(orderId).Count == 2, "recorded result republished");

        var results = fixture.Probe.Of<PaymentCompleted>(orderId);
        Assert.Equal(results[0].PaymentId, results[1].PaymentId);
        Assert.Equal(300m, await BalanceAsync(customerId));
        await using var db = fixture.CreatePaymentDb();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == orderId));
    }

    // --- c) Eşzamanlı iki ProcessPayment: unique ihlali (23505) + retry yolu ----------------------------------------

    [Fact]
    public async Task Two_concurrent_process_payments_for_the_same_order_charge_once_via_unique_violation_and_retry()
    {
        var customerId = await fixture.SeedWalletAsync(500);
        var orderId = Guid.NewGuid();
        var command = Pay(orderId, customerId, 150);
        var uniqueViolationsBefore = fixture.PaymentsDbFailures.Count("23505");

        // Test cüzdan satırını tutar; iki consumer da "kayıt yok" görüp aynı kilitte bekler → çakışma garanti.
        await using var holder = new NpgsqlConnection(fixture.PaymentConnectionString);
        await holder.OpenAsync();
        await using var transaction = await holder.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM \"Wallets\" WHERE \"CustomerId\" = @id FOR UPDATE", holder, transaction))
        {
            lockRow.Parameters.AddWithValue("id", customerId);
            await lockRow.ExecuteScalarAsync();
        }

        await fixture.Probe.SendCommandAsync(command, QueueNames.ProcessPayment, Guid.NewGuid());
        await fixture.Probe.SendCommandAsync(command, QueueNames.ProcessPayment, Guid.NewGuid());
        await Eventually.UntilAsync(
            async () => await fixture.LockWaitersAsync("payment_db") >= 2,
            "both consumers are blocked on the wallet row lock");

        await transaction.CommitAsync();

        await Eventually.UntilAsync(() => fixture.Probe.Of<PaymentCompleted>(orderId).Count == 2, "both commands answered");

        var results = fixture.Probe.Of<PaymentCompleted>(orderId);
        Assert.Equal(results[0].PaymentId, results[1].PaymentId);
        Assert.Equal(350m, await BalanceAsync(customerId));
        await using var db = fixture.CreatePaymentDb();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == orderId && p.Status == PaymentStatus.Completed));

        // İkinci insert 23505 aldı (bakiye düşüşüyle birlikte geri alındı), retry kayıtlı sonucu buldu.
        Assert.True(
            fixture.PaymentsDbFailures.Count("23505") > uniqueViolationsBefore,
            $"expected a unique violation (23505) on the losing consumer (seen: {fixture.PaymentsDbFailures.Summary})");
    }

    /// <summary>
    /// Tek iş etkisi = tek sonuç mesajı (tek MessageId). MassTransit outbox at-least-once çalışır: yinelenen teslimde
    /// henüz "delivered" işaretlenmemiş outbox mesajı aynı MessageId ile yeniden iletilebilir; alıcılar (saga inbox)
    /// MessageId ile ayıklar. Bu yüzden ham event sayısı değil, benzersiz MessageId sayısı doğrulanır.
    /// </summary>
    private void AssertSingleDistinctMessage<T>(Guid orderId)
        where T : class =>
        Assert.Single(fixture.Probe.ReceivedOf<T>(orderId).Select(r => r.MessageId).Distinct());

    private async Task<decimal> BalanceAsync(Guid customerId)
    {
        await using var db = fixture.CreatePaymentDb();
        return await db.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync();
    }

    private static ReserveStock Reserve(Guid orderId, Guid productId, int quantity) => new()
    {
        OrderId = orderId,
        Items = [new OrderItemDto { ProductId = productId, Quantity = quantity, UnitPrice = 10 }],
        RequestedAt = DateTimeOffset.UtcNow,
    };

    private static ProcessPayment Pay(Guid orderId, Guid customerId, decimal amount) => new()
    {
        OrderId = orderId,
        CustomerId = customerId,
        Amount = amount,
        Currency = "TRY",
        RequestedAt = DateTimeOffset.UtcNow,
    };
}
