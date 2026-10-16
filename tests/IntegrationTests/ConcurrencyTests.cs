using BuildingBlocks.Messaging;
using Inventory.Domain.Reservations;
using Inventory.Infrastructure.Consumers;
using IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Payments.Domain.Transactions;
using Payments.Infrastructure.Consumers;
using Shared.Contracts.Common;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace IntegrationTests;

/// <summary>
/// Gerçek RabbitMQ + Postgres üzerinde paralel işlem. Asıl "negatife düşmez" garantisi DB'deki CHECK kısıtıdır;
/// ValueWatcher yoklaması yardımcı kanıttır.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ConcurrencyTests(IntegrationFixture fixture)
{
    [Fact]
    public void Consumers_are_configured_to_run_in_parallel()
    {
        Assert.Equal(16, ReserveStockConsumerDefinition.ConcurrencyLimit);
        Assert.Equal(16, new ReserveStockConsumerDefinition().ConcurrentMessageLimit);
        Assert.Equal(16, new ProcessPaymentConsumerDefinition().ConcurrentMessageLimit);
        Assert.True(new ReserveStockConsumerDefinition().ConcurrentMessageLimit > 1);
    }

    [Fact]
    public async Task Fifty_parallel_reservations_for_ten_units_reserve_exactly_ten()
    {
        var productId = await fixture.SeedProductAsync(10);
        var orderIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        Assert.True(await fixture.CheckConstraintCountAsync(fixture.StockConnectionString, "Products") >= 1, "CHECK constraint missing");

        await using var watcher = ValueWatcher.Start(async ct =>
        {
            await using var db = fixture.CreateStockDb();
            return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => (decimal)p.AvailableQuantity).SingleAsync(ct);
        });

        await Task.WhenAll(orderIds.Select(orderId => fixture.Probe.SendCommandAsync(
            Reserve(orderId, (productId, 1)), QueueNames.ReserveStock)));

        await Eventually.UntilAsync(
            () => orderIds.Count(o => fixture.Probe.Of<StockReserved>(o).Count + fixture.Probe.Of<StockReservationFailed>(o).Count > 0) == 50,
            "all 50 reservations answered");

        var reserved = orderIds.Count(o => fixture.Probe.Of<StockReserved>(o).Count == 1);
        var rejected = orderIds.Where(o => fixture.Probe.Of<StockReservationFailed>(o).Count == 1).ToList();
        Assert.Equal(10, reserved);
        Assert.Equal(40, rejected.Count);
        Assert.All(rejected, o => Assert.Equal(ReasonCodes.InsufficientStock, fixture.Probe.Of<StockReservationFailed>(o).Single().ReasonCode));

        await using var db = fixture.CreateStockDb();
        Assert.Equal(0, await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync());
        Assert.Equal(10, await db.Reservations.CountAsync(r => orderIds.Contains(r.OrderId) && r.Status == ReservationStatus.Reserved));
        Assert.Equal(40, await db.Reservations.CountAsync(r => orderIds.Contains(r.OrderId) && r.Status == ReservationStatus.Rejected));

        Assert.True(watcher.Samples > 0);
        Assert.True(watcher.Min >= 0, $"stock was observed at {watcher.Min}");

        // Gerçekten paralel çalıştı: consumer'lar aynı anda birden fazla mesaj işledi, limiti aşmadı.
        var peak = fixture.InventoryConsumers.PeakConcurrency<ReserveStock>();
        Assert.InRange(peak, 2, ReserveStockConsumerDefinition.ConcurrencyLimit);
    }

    [Fact]
    public async Task Opposite_item_orders_across_products_do_not_deadlock()
    {
        var a = await fixture.SeedProductAsync(1000);
        var b = await fixture.SeedProductAsync(1000);
        var c = await fixture.SeedProductAsync(1000);
        var permutations = new[] { (a, b, c), (c, b, a), (b, c, a), (a, c, b), (c, a, b), (b, a, c) };
        var orderIds = Enumerable.Range(0, 60).Select(_ => Guid.NewGuid()).ToList();

        var deadlocksBefore = await fixture.DeadlockCountAsync("stock_db");
        var observedBefore = fixture.InventoryDbFailures.Count("40P01");

        await Task.WhenAll(orderIds.Select((orderId, i) =>
        {
            var (x, y, z) = permutations[i % permutations.Length];
            return fixture.Probe.SendCommandAsync(Reserve(orderId, (x, 1), (y, 1), (z, 1)), QueueNames.ReserveStock);
        }));

        Xunit.Sdk.XunitException? incomplete = null;
        try
        {
            await Eventually.UntilAsync(
                () => orderIds.All(o => fixture.Probe.Of<StockReserved>(o).Count == 1),
                "all 60 mixed-order reservations reserved");
        }
        catch (Xunit.Sdk.XunitException exception)
        {
            incomplete = exception;
        }

        // Retry deadlock'u gizleyebileceği için iki ayrı kanıt: EF'in gördüğü her 40P01 komut hatası ve DB'nin deadlock
        // sayacı. Önce deadlock kanıtı raporlanır; yoksa tamamlanmama yalnızca zaman aşımı gibi görünürdü.
        var observed = fixture.InventoryDbFailures.Count("40P01") - observedBefore;
        if (observed > 0)
        {
            var statDelta = 0L;
            try
            {
                // Diğer backend'lerin istatistik payı gecikmeli yazılır; yalnızca hata durumunda beklenir.
                await Eventually.UntilAsync(
                    async () => (statDelta = await fixture.DeadlockCountAsync("stock_db") - deadlocksBefore) > 0,
                    "pg_stat_database.deadlocks to reflect the deadlocks",
                    TimeSpan.FromSeconds(20));
            }
            catch (Xunit.Sdk.XunitException)
            {
                // Sayaç henüz görünmüyor; 40P01 kanıtı yeterli.
            }

            Assert.Fail($"Deadlocks detected: {observed} x 40P01 command failures, pg_stat_database.deadlocks +{statDelta}.");
        }

        if (incomplete is not null)
        {
            throw incomplete;
        }

        await using var db = fixture.CreateStockDb();
        foreach (var id in new[] { a, b, c })
        {
            Assert.Equal(940, await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.AvailableQuantity).SingleAsync());
        }

        Assert.Equal(deadlocksBefore, await fixture.DeadlockCountAsync("stock_db"));
    }

    [Fact]
    public async Task Twenty_parallel_payments_exceeding_the_balance_never_overdraw_the_wallet()
    {
        const decimal initial = 1000m;
        const decimal price = 150m;
        var customerId = await fixture.SeedWalletAsync(initial);
        var orderIds = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();
        Assert.True(await fixture.CheckConstraintCountAsync(fixture.PaymentConnectionString, "Wallets") >= 1, "CHECK constraint missing");

        await using var watcher = ValueWatcher.Start(async ct =>
        {
            await using var db = fixture.CreatePaymentDb();
            return await db.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync(ct);
        });

        await Task.WhenAll(orderIds.Select(orderId => fixture.Probe.SendCommandAsync(
            new ProcessPayment { OrderId = orderId, CustomerId = customerId, Amount = price, Currency = "TRY", RequestedAt = DateTimeOffset.UtcNow },
            QueueNames.ProcessPayment)));

        await Eventually.UntilAsync(
            () => orderIds.Count(o => fixture.Probe.Of<PaymentCompleted>(o).Count + fixture.Probe.Of<PaymentFailed>(o).Count > 0) == 20,
            "all 20 payments answered");

        var completed = orderIds.Count(o => fixture.Probe.Of<PaymentCompleted>(o).Count == 1);
        var declined = orderIds.Where(o => fixture.Probe.Of<PaymentFailed>(o).Count == 1).ToList();
        Assert.Equal(6, completed);
        Assert.Equal(14, declined.Count);
        Assert.All(declined, o => Assert.Equal(ReasonCodes.PaymentDeclined, fixture.Probe.Of<PaymentFailed>(o).Single().ReasonCode));

        await using var db = fixture.CreatePaymentDb();
        var balance = await db.Wallets.AsNoTracking().Where(w => w.CustomerId == customerId).Select(w => w.Balance).SingleAsync();
        var charged = (await db.Payments.AsNoTracking()
            .Where(p => orderIds.Contains(p.OrderId) && p.Status == PaymentStatus.Completed)
            .Select(p => p.Amount)
            .ToListAsync()).Sum();

        Assert.True(balance >= 0);
        Assert.Equal(initial, balance + charged);
        Assert.Equal(completed * price, charged);

        Assert.True(watcher.Samples > 0);
        Assert.True(watcher.Min >= 0, $"balance was observed at {watcher.Min}");
        Assert.InRange(fixture.PaymentsConsumers.PeakConcurrency<ProcessPayment>(), 2, ProcessPaymentConsumerDefinition.ConcurrencyLimit);
    }

    private static ReserveStock Reserve(Guid orderId, params (Guid ProductId, int Quantity)[] items) => new()
    {
        OrderId = orderId,
        Items = items.Select(i => new OrderItemDto { ProductId = i.ProductId, Quantity = i.Quantity, UnitPrice = 10 }).ToList(),
        RequestedAt = DateTimeOffset.UtcNow,
    };
}
