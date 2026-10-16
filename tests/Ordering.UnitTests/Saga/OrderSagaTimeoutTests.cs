using Ordering.Domain.Orders;
using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace Ordering.UnitTests.Saga;

/// <summary>
/// Ödeme timeout'u. MassTransit test harness'ında zamanı ileri alan bir API yok; bu yüzden timeout
/// 250 ms'ye kısaltılır ve in-memory delayed scheduler gerçek (kısa) gecikmeyle teslim eder.
/// </summary>
public sealed class OrderSagaTimeoutTests : OrderSagaFixture
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(250);

    protected override TimeSpan PaymentTimeout => _timeout;

    [Fact]
    public async Task ProcessPayment_is_sent_with_time_to_live_equal_to_payment_timeout()
    {
        var orderId = await ReachAwaitingPaymentAsync();

        var sent = Harness.Sent.Select<ProcessPayment>().Single(x => x.Context.Message.OrderId == orderId);
        Assert.Equal(_timeout, sent.Context.TimeToLive);
    }

    [Fact]
    public async Task Timeout_releases_stock_and_fails_order_with_PAYMENT_TIMEOUT()
    {
        var orderId = await ReachAwaitingPaymentAsync();

        await AssertStateAsync(orderId, Machine.Compensating);
        await WaitUntilAsync(() => SentCount<ReleaseStock>(orderId) == 1, "ReleaseStock was not sent after timeout.");
        Assert.Equal(OrderStatus.Pending, GetOrder(orderId).Status);

        await PublishAndWaitAsync(new StockReleased { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.Failed);

        await WaitUntilAsync(() => PublishedCount<OrderFailed>(orderId) == 1, "OrderFailed was not published.");
        var failed = Harness.Published.Select<OrderFailed>().Single(x => x.Context.Message.OrderId == orderId).Context.Message;
        Assert.Equal(ReasonCodes.PaymentTimeout, failed.ReasonCode);
        Assert.Equal(OrderStatus.Failed, GetOrder(orderId).Status);
        Assert.Equal(ReasonCodes.PaymentTimeout, GetOrder(orderId).ReasonCode);
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Late_PaymentCompleted_while_compensating_requests_a_single_refund()
    {
        var orderId = await ReachAwaitingPaymentAsync();
        await AssertStateAsync(orderId, Machine.Compensating);

        await PublishAndWaitAsync(PaymentCompleted(orderId, 399.90m));
        await PublishAndWaitAsync(PaymentCompleted(orderId, 399.90m));

        await WaitUntilAsync(() => SentCount<RefundPayment>(orderId) == 1, "RefundPayment was not sent.");
        var refund = Harness.Sent.Select<RefundPayment>().Single(x => x.Context.Message.OrderId == orderId).Context.Message;
        Assert.Equal(399.90m, refund.Amount);
        Assert.Equal("TRY", refund.Currency);
        Assert.Contains(ReasonCodes.PaymentTimeout, refund.Reason, StringComparison.Ordinal);

        // Compensation devam eder: stok iadesi gelince sipariş yine Failed olur.
        await PublishAndWaitAsync(new StockReleased { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.Failed);
        Assert.Equal(OrderStatus.Failed, GetOrder(orderId).Status);

        await PublishAndWaitAsync(new PaymentRefunded
        {
            OrderId = orderId,
            Amount = 399.90m,
            Currency = "TRY",
            OccurredAt = DateTimeOffset.UtcNow,
        });
        Assert.NotNull(GetSaga(orderId).RefundedAt);
        Assert.Equal(1, SentCount<RefundPayment>(orderId));
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Late_PaymentCompleted_after_order_failed_requests_refund()
    {
        var orderId = await ReachAwaitingPaymentAsync();
        await AssertStateAsync(orderId, Machine.Compensating);
        await PublishAndWaitAsync(new StockReleased { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.Failed);

        await PublishAndWaitAsync(PaymentCompleted(orderId, 399.90m));

        await WaitUntilAsync(() => SentCount<RefundPayment>(orderId) == 1, "RefundPayment was not sent.");
        await AssertStateAsync(orderId, Machine.Failed);
        Assert.Equal(OrderStatus.Failed, GetOrder(orderId).Status);
        AssertNoSagaFaults();
    }

    [Fact]
    public async Task Payment_before_timeout_completes_and_late_timeout_has_no_effect()
    {
        var orderId = await ReachAwaitingPaymentAsync(waitForTimeout: false);

        await PublishAndWaitAsync(PaymentCompleted(orderId, 399.90m));
        await AssertStateAsync(orderId, Machine.Completed);

        // Timeout süresinin ötesinde bekle: iptal edilmiş/gecikmiş timeout siparişi etkilememeli.
        await Task.Delay(_timeout * 3);

        await AssertStateAsync(orderId, Machine.Completed);
        Assert.Equal(OrderStatus.Completed, GetOrder(orderId).Status);
        Assert.Equal(0, SentCount<ReleaseStock>(orderId));
        Assert.Equal(0, SentCount<RefundPayment>(orderId));
        AssertNoSagaFaults();
    }

    private async Task<Guid> ReachAwaitingPaymentAsync(bool waitForTimeout = true)
    {
        var orderId = await SubmitOrderAsync();
        await PublishAndWaitAsync(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        if (!waitForTimeout)
        {
            await AssertStateAsync(orderId, Machine.AwaitingPayment);
        }

        return orderId;
    }

    private static PaymentCompleted PaymentCompleted(Guid orderId, decimal amount) => new()
    {
        OrderId = orderId,
        PaymentId = Guid.NewGuid(),
        Amount = amount,
        Currency = "TRY",
        OccurredAt = DateTimeOffset.UtcNow,
    };
}
