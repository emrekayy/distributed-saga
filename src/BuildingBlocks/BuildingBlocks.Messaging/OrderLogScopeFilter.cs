using MassTransit;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Mesaj <c>Guid OrderId</c> taşıyorsa tüketim süresince log scope'una <c>OrderId</c> ekler; böylece consumer/saga log satırlarının
/// hepsi OrderId ile süzülebilir. Contract'lara ortak interface eklenmez (yeni exchange demek olurdu, ADR-006); özellik adı yansıtmayla
/// bir kez çözülür.
/// </summary>
public sealed class OrderLogScopeFilter<T>(ILogger<OrderLogScopeFilter<T>> logger) : IFilter<ConsumeContext<T>>
    where T : class
{
    private static readonly System.Reflection.PropertyInfo? _orderIdProperty =
        typeof(T).GetProperty("OrderId") is { } property && property.PropertyType == typeof(Guid) ? property : null;

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        if (_orderIdProperty?.GetValue(context.Message) is Guid orderId)
        {
            using (logger.BeginScope("OrderId:{OrderId}", orderId))
            {
                await next.Send(context);
            }

            return;
        }

        await next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("orderLogScope");
}
