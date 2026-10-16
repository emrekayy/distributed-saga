using MassTransit;
using Ordering.Application.Orders;

namespace Ordering.Application.Saga;

/// <summary>
/// Order aggregate'ini Completed yapar. Saga state, aggregate ve outbox aynı DbContext'te olduğundan
/// değişiklik saga repository'nin SaveChanges'iyle tek transaction'da commit edilir (burada SaveChanges yok).
/// </summary>
public sealed class MarkOrderCompletedActivity<TMessage>(IOrderRepository orders) : IStateMachineActivity<OrderState, TMessage>
    where TMessage : class
{
    public void Probe(ProbeContext context) => context.CreateScope("mark-order-completed");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<OrderState, TMessage> context, IBehavior<OrderState, TMessage> next)
    {
        var order = await orders.GetByIdAsync(context.Saga.CorrelationId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Order {context.Saga.CorrelationId} not found for saga.");
        order.Complete();
        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<OrderState, TMessage, TException> context, IBehavior<OrderState, TMessage> next)
        where TException : Exception =>
        next.Faulted(context);
}

/// <summary>
/// Order aggregate'ini saga state'teki sebep koduyla Failed yapar (StockReservationFailed veya
/// compensation sonrası StockReleased). SaveChanges saga repository'dedir.
/// </summary>
public sealed class MarkOrderFailedActivity<TMessage>(IOrderRepository orders) : IStateMachineActivity<OrderState, TMessage>
    where TMessage : class
{
    public void Probe(ProbeContext context) => context.CreateScope("mark-order-failed");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);

    public async Task Execute(BehaviorContext<OrderState, TMessage> context, IBehavior<OrderState, TMessage> next)
    {
        var saga = context.Saga;
        var order = await orders.GetByIdAsync(saga.CorrelationId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Order {saga.CorrelationId} not found for saga.");
        order.Fail(saga.FailureReasonCode!, saga.FailureReason ?? string.Empty);
        await next.Execute(context);
    }

    public Task Faulted<TException>(
        BehaviorExceptionContext<OrderState, TMessage, TException> context, IBehavior<OrderState, TMessage> next)
        where TException : Exception =>
        next.Faulted(context);
}
