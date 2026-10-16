using MassTransit;
using Microsoft.Extensions.Logging;

namespace Ordering.Application.Saga;

/// <summary>Saga geçişlerini tek satırla loglar: OrderId, eski -> yeni state.</summary>
internal sealed partial class TransitionLogObserver(ILogger logger) : IStateObserver<OrderState>
{
    public Task StateChanged(BehaviorContext<OrderState> context, State currentState, State previousState)
    {
        // Yeni instance önce Initial -> Initial olarak kaydedilir; gerçek bir geçiş olmadığı için loglanmaz.
        var previous = previousState?.Name ?? "Initial";
        if (previous != currentState.Name)
        {
            LogTransition(logger, context.Saga.CorrelationId, previous, currentState.Name);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} saga state: {PreviousState} -> {CurrentState}")]
    private static partial void LogTransition(ILogger logger, Guid orderId, string previousState, string currentState);
}
