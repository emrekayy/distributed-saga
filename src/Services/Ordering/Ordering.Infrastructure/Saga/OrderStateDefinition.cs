using BuildingBlocks.Messaging;
using MassTransit;
using Ordering.Application.Saga;
using Ordering.Domain.Orders;
using Ordering.Infrastructure.Persistence;

namespace Ordering.Infrastructure.Saga;

/// <summary>
/// Saga endpoint'i: dışta retry (geçici DB hataları), içte EF outbox. Retry dışta olduğu için her deneme
/// yeni bir transaction'la başlar; inbox (InboxState) MessageId tekrarını, outbox giden mesajları
/// saga state ile aynı transaction'a bağlar.
/// </summary>
public sealed class OrderStateDefinition : SagaDefinition<OrderState>
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderState> sagaConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.UseTransientDatabaseRetry(typeof(OrderDomainException)));
        endpointConfigurator.UseEntityFrameworkOutbox<OrderDbContext>(context);
    }
}
