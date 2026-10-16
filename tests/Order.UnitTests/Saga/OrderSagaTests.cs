using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace Order.UnitTests.Saga;

using Order.Domain.Orders;

public sealed class OrderSagaTests : OrderSagaFixture
{
    [Fact]
    public async Task Happy_path_reserves_stock_charges_payment_and_completes_order()
    {
        var orderId = await SubmitOrderAsync();

        var reserve = await WaitSentAsync<ReserveStock>(orderId);
        Assert.Equal(2, reserve.Items.Count);

        await Harness.Bus.Publish(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.AwaitingPayment);

        var payment = await WaitSentAsync<ProcessPayment>(orderId);
        Assert.Equal(399.90m, payment.Amount);
        Assert.Equal("TRY", payment.Currency);

        await Harness.Bus.Publish(Completed(orderId));
        await AssertStateAsync(orderId, Machine.Completed);

        await WaitPublishedAsync<OrderCompleted>(orderId);
        Assert.Equal(OrderStatus.Completed, GetOrder(orderId).Status);
        Assert.Equal(0, SentCount<ReleaseStock>(orderId));
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Payment_amount_comes_from_OrderSubmitted_total_not_recomputed()
    {
        var orderId = await SubmitOrderAsync(totalAmountOverride: 123.45m);

        await Harness.Bus.Publish(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });

        var payment = await WaitSentAsync<ProcessPayment>(orderId);
        Assert.Equal(123.45m, payment.Amount);
    }

    [Fact]
    public async Task Insufficient_stock_fails_order_without_compensation()
    {
        var orderId = await SubmitOrderAsync();

        await Harness.Bus.Publish(new StockReservationFailed
        {
            OrderId = orderId,
            ReasonCode = ReasonCodes.InsufficientStock,
            Reason = "Not enough stock for product A",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await AssertStateAsync(orderId, Machine.Failed);

        var failed = await WaitPublishedAsync<OrderFailed>(orderId);
        Assert.Equal(ReasonCodes.InsufficientStock, failed.ReasonCode);

        var order = GetOrder(orderId);
        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal(ReasonCodes.InsufficientStock, order.ReasonCode);
        Assert.Equal(0, SentCount<ProcessPayment>(orderId));
        Assert.Equal(0, SentCount<ReleaseStock>(orderId));
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Payment_failure_releases_stock_then_fails_order()
    {
        var orderId = await SubmitOrderAsync();
        await Harness.Bus.Publish(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.AwaitingPayment);

        await Harness.Bus.Publish(new PaymentFailed
        {
            OrderId = orderId,
            ReasonCode = ReasonCodes.PaymentDeclined,
            Reason = "Card declined",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await AssertStateAsync(orderId, Machine.Compensating);
        await WaitSentAsync<ReleaseStock>(orderId);

        // Compensation tamamlanmadan sipariş Failed olmaz.
        Assert.Equal(OrderStatus.Pending, GetOrder(orderId).Status);
        Assert.Equal(0, PublishedCount<OrderFailed>(orderId));

        await Harness.Bus.Publish(new StockReleased { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.Failed);

        var failed = await WaitPublishedAsync<OrderFailed>(orderId);
        Assert.Equal(ReasonCodes.PaymentDeclined, failed.ReasonCode);
        Assert.Equal("Card declined", failed.Reason);
        Assert.Equal(OrderStatus.Failed, GetOrder(orderId).Status);
        Assert.Equal(ReasonCodes.PaymentDeclined, GetOrder(orderId).ReasonCode);
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Duplicate_PaymentCompleted_is_ignored_after_completion()
    {
        var orderId = await SubmitOrderAsync();
        await Harness.Bus.Publish(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.AwaitingPayment);

        await PublishAndWaitAsync(Completed(orderId));
        await PublishAndWaitAsync(Completed(orderId));

        await AssertStateAsync(orderId, Machine.Completed);
        Assert.Equal(1, PublishedCount<OrderCompleted>(orderId));
        Assert.Equal(OrderStatus.Completed, GetOrder(orderId).Status);
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Duplicate_OrderSubmitted_and_StockReserved_do_not_resend_commands()
    {
        var orderId = await SubmitOrderAsync();
        await PublishAndWaitAsync(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await PublishAndWaitAsync(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });

        await AssertStateAsync(orderId, Machine.AwaitingPayment);
        Assert.Equal(1, SentCount<ReserveStock>(orderId));
        Assert.Equal(1, SentCount<ProcessPayment>(orderId));
        AssertNoSagaFaults();
    }

    private static PaymentCompleted Completed(Guid orderId) => new()
    {
        OrderId = orderId,
        PaymentId = Guid.NewGuid(),
        Amount = 399.90m,
        Currency = "TRY",
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private async Task<T> WaitSentAsync<T>(Guid orderId)
        where T : class
    {
        await WaitUntilAsync(() => SentCount<T>(orderId) > 0, $"{typeof(T).Name} was not sent.");
        return Harness.Sent.Select<T>().Select(x => x.Context.Message).First(m => OrderIdOf(m) == orderId);
    }

    private async Task<T> WaitPublishedAsync<T>(Guid orderId)
        where T : class
    {
        await WaitUntilAsync(() => PublishedCount<T>(orderId) > 0, $"{typeof(T).Name} was not published.");
        return Harness.Published.Select<T>().Select(x => x.Context.Message).First(m => OrderIdOf(m) == orderId);
    }
}
