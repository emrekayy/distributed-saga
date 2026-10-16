using MassTransit;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace Order.Application.Saga;

/// <summary>
/// Sipariş orchestration saga'sı (CorrelationId = OrderId).
/// <para>
/// Completed ve Failed normal state'lerdir; Finalize/SetCompletedWhenFinalized kullanılmaz, kayıt denetim
/// izi olarak kalır ve geç gelen event'ler bu state'lerde de karşılanır. Her state'te beklenmeyen tüm
/// event'ler açıkça Ignore edilir; çift/geç teslim saga'yı hata kuyruğuna düşürmez.
/// </para>
/// </summary>
public sealed class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    private readonly TimeProvider _clock;

    public OrderStateMachine(TimeProvider clock)
    {
        _clock = clock;

        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReserved, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReservationFailed, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReleased, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentCompleted, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentRefunded, e => e.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderSubmitted)
                .Then(context =>
                {
                    var saga = context.Saga;
                    saga.CustomerId = context.Message.CustomerId;
                    saga.Items = context.Message.Items.ToList();
                    saga.TotalAmount = context.Message.TotalAmount;
                    saga.Currency = context.Message.Currency;
                    saga.CreatedAt = saga.UpdatedAt = Now;
                })
                .Send(context => new ReserveStock
                {
                    OrderId = context.Saga.CorrelationId,
                    Items = context.Saga.Items,
                    RequestedAt = Now,
                })
                .TransitionTo(AwaitingStock));

        During(AwaitingStock,
            When(StockReserved)
                .Then(Touch)
                .Send(context => new ProcessPayment
                {
                    OrderId = context.Saga.CorrelationId,
                    CustomerId = context.Saga.CustomerId,
                    // OrderSubmitted.TotalAmount'tan; saga tutarı yeniden hesaplamaz.
                    Amount = context.Saga.TotalAmount,
                    Currency = context.Saga.Currency,
                    RequestedAt = Now,
                })
                .TransitionTo(AwaitingPayment),
            When(StockReservationFailed)
                .Then(context => RecordFailure(context.Saga, context.Message.ReasonCode, context.Message.Reason))
                .Activity(x => x.OfType<MarkOrderFailedActivity<StockReservationFailed>>())
                .Publish(context => CreateOrderFailed(context.Saga))
                .TransitionTo(Failed),
            Ignore(OrderSubmitted),
            Ignore(StockReleased),
            Ignore(PaymentCompleted),
            Ignore(PaymentFailed),
            Ignore(PaymentRefunded));

        During(AwaitingPayment,
            When(PaymentCompleted)
                .Then(Touch)
                .Activity(x => x.OfType<MarkOrderCompletedActivity<PaymentCompleted>>())
                .Publish(context => new OrderCompleted { OrderId = context.Saga.CorrelationId, OccurredAt = Now })
                .TransitionTo(Completed),
            When(PaymentFailed)
                .Then(context => RecordFailure(context.Saga, context.Message.ReasonCode, context.Message.Reason))
                .Send(context => new ReleaseStock { OrderId = context.Saga.CorrelationId, RequestedAt = Now })
                .TransitionTo(Compensating),
            Ignore(OrderSubmitted),
            Ignore(StockReserved),
            Ignore(StockReservationFailed),
            Ignore(StockReleased),
            Ignore(PaymentRefunded));

        During(Compensating,
            When(StockReleased)
                .Then(Touch)
                .Activity(x => x.OfType<MarkOrderFailedActivity<StockReleased>>())
                .Publish(context => CreateOrderFailed(context.Saga))
                .TransitionTo(Failed),
            Ignore(OrderSubmitted),
            Ignore(StockReserved),
            Ignore(StockReservationFailed),
            Ignore(PaymentCompleted),
            Ignore(PaymentFailed),
            Ignore(PaymentRefunded));

        During(Completed,
            Ignore(OrderSubmitted),
            Ignore(StockReserved),
            Ignore(StockReservationFailed),
            Ignore(StockReleased),
            Ignore(PaymentCompleted),
            Ignore(PaymentFailed),
            Ignore(PaymentRefunded));

        During(Failed,
            Ignore(OrderSubmitted),
            Ignore(StockReserved),
            Ignore(StockReservationFailed),
            Ignore(StockReleased),
            Ignore(PaymentCompleted),
            Ignore(PaymentFailed),
            Ignore(PaymentRefunded));
    }

    public State AwaitingStock { get; private set; } = null!;

    public State AwaitingPayment { get; private set; } = null!;

    public State Compensating { get; private set; } = null!;

    public State Completed { get; private set; } = null!;

    public State Failed { get; private set; } = null!;

    public Event<OrderSubmitted> OrderSubmitted { get; private set; } = null!;

    public Event<StockReserved> StockReserved { get; private set; } = null!;

    public Event<StockReservationFailed> StockReservationFailed { get; private set; } = null!;

    public Event<StockReleased> StockReleased { get; private set; } = null!;

    public Event<PaymentCompleted> PaymentCompleted { get; private set; } = null!;

    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;

    public Event<PaymentRefunded> PaymentRefunded { get; private set; } = null!;

    private DateTimeOffset Now => _clock.GetUtcNow();

    private void Touch<TMessage>(BehaviorContext<OrderState, TMessage> context)
        where TMessage : class =>
        context.Saga.UpdatedAt = Now;

    private void RecordFailure(OrderState saga, string reasonCode, string reason)
    {
        saga.FailureReasonCode = reasonCode;
        saga.FailureReason = reason;
        saga.UpdatedAt = Now;
    }

    private OrderFailed CreateOrderFailed(OrderState saga) => new()
    {
        OrderId = saga.CorrelationId,
        ReasonCode = saga.FailureReasonCode!,
        Reason = saga.FailureReason!,
        OccurredAt = Now,
    };
}
