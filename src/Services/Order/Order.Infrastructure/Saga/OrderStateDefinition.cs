using BuildingBlocks.Messaging;
using MassTransit;
using Order.Application.Saga;
using Order.Domain.Orders;
using Order.Infrastructure.Persistence;

namespace Order.Infrastructure.Saga;

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
