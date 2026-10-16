using MassTransit;
using Order.Application.Saga;
using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Events;

namespace Order.UnitTests.Saga;

/// <summary>
/// Her state'te, o state'te işlenmeyen tüm event'ler gönderilir: saga fault üretmemeli (hata kuyruğu yok),
/// state değişmemeli ve yeni bir command/event çıkmamalı. Yeni bir event eklenip bir state'te Ignore
/// unutulursa UnhandledEventException ile bu test kırılır.
/// </summary>
public class OrderSagaIgnoreMatrixTests : OrderSagaFixture
{
    /// <summary>State → o state'te davranışı olan (Ignore edilmeyen) event'ler.</summary>
    private static readonly Dictionary<string, string[]> _handled = new()
    {
        ["AwaitingStock"] = [nameof(StockReserved), nameof(StockReservationFailed)],
        ["AwaitingPayment"] = [nameof(PaymentCompleted), nameof(PaymentFailed), nameof(PaymentTimeoutExpired)],
        // Geç PaymentCompleted → RefundPayment; PaymentRefunded → RefundedAt kaydı (state değişmez).
        ["Compensating"] = [nameof(StockReleased), nameof(PaymentCompleted), nameof(PaymentRefunded)],
        ["Completed"] = [],
        ["Failed"] = [nameof(PaymentCompleted), nameof(PaymentRefunded)],
    };

    public static TheoryData<string> States => new(_handled.Keys);

    [Theory]
    [MemberData(nameof(States))]
    public async Task Unexpected_events_are_ignored(string stateName)
    {
        var orderId = await DriveToAsync(stateName);
        var state = Machine.GetState(stateName);
        var sentBefore = Harness.Sent.Select<object>().Count();
        var publishedBefore = Harness.Published.Select<object>().Count();

        var ignored = AllEvents(orderId).Where(e => !_handled[stateName].Contains(e.Name)).ToList();
        Assert.NotEmpty(ignored);
        foreach (var (_, publish) in ignored)
        {
            await publish();
        }

        await AssertStateAsync(orderId, state);
        AssertNoSagaFaults();
        Assert.Equal(sentBefore, Harness.Sent.Select<object>().Count());
        // Testin kendi publish ettiği event'ler hariç saga yeni bir şey publish etmemeli.
        Assert.Equal(publishedBefore + ignored.Count, Harness.Published.Select<object>().Count());
    }

    private async Task<Guid> DriveToAsync(string stateName)
    {
        var orderId = await SubmitOrderAsync();
        var now = DateTimeOffset.UtcNow;

        switch (stateName)
        {
            case "AwaitingStock":
                break;
            case "Failed":
                await PublishAndWaitAsync(new StockReservationFailed
                {
                    OrderId = orderId, ReasonCode = ReasonCodes.InsufficientStock, Reason = "none", OccurredAt = now,
                });
                break;
            default:
                await PublishAndWaitAsync(new StockReserved { OrderId = orderId, OccurredAt = now });
                if (stateName == "Completed")
                {
                    await PublishAndWaitAsync(PaymentCompleted(orderId));
                }
                else if (stateName == "Compensating")
                {
                    await PublishAndWaitAsync(new PaymentFailed
                    {
                        OrderId = orderId, ReasonCode = ReasonCodes.PaymentDeclined, Reason = "declined", OccurredAt = now,
                    });
                }

                break;
        }

        await AssertStateAsync(orderId, Machine.GetState(stateName));
        return orderId;
    }

    private IEnumerable<(string Name, Func<Task> Publish)> AllEvents(Guid orderId)
    {
        var now = DateTimeOffset.UtcNow;
        yield return (nameof(OrderSubmitted), () => PublishAndWaitAsync(new OrderSubmitted
        {
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            Items = [new OrderItemDto { ProductId = ProductA, Quantity = 1, UnitPrice = 1m }],
            TotalAmount = 1m,
            Currency = "TRY",
            OccurredAt = now,
        }));
        yield return (nameof(StockReserved), () => PublishAndWaitAsync(new StockReserved { OrderId = orderId, OccurredAt = now }));
        yield return (nameof(StockReservationFailed), () => PublishAndWaitAsync(new StockReservationFailed
        {
            OrderId = orderId, ReasonCode = ReasonCodes.InsufficientStock, Reason = "late", OccurredAt = now,
        }));
        yield return (nameof(StockReleased), () => PublishAndWaitAsync(new StockReleased { OrderId = orderId, OccurredAt = now }));
        yield return (nameof(PaymentCompleted), () => PublishAndWaitAsync(PaymentCompleted(orderId)));
        yield return (nameof(PaymentFailed), () => PublishAndWaitAsync(new PaymentFailed
        {
            OrderId = orderId, ReasonCode = ReasonCodes.PaymentDeclined, Reason = "late", OccurredAt = now,
        }));
        yield return (nameof(PaymentRefunded), () => PublishAndWaitAsync(new PaymentRefunded
        {
            OrderId = orderId, Amount = 399.90m, Currency = "TRY", OccurredAt = now,
        }));
        // Unschedule ile yarışan geç bir timeout'u simüle eder.
        yield return (nameof(PaymentTimeoutExpired), () => PublishAndWaitAsync(new PaymentTimeoutExpired { OrderId = orderId }));
    }

    private static PaymentCompleted PaymentCompleted(Guid orderId) => new()
    {
        OrderId = orderId,
        PaymentId = Guid.NewGuid(),
        Amount = 399.90m,
        Currency = "TRY",
        OccurredAt = DateTimeOffset.UtcNow,
    };
}
