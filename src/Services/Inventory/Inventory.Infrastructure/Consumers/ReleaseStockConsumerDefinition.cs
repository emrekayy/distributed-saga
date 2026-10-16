using BuildingBlocks.Messaging;
using Inventory.Application.Reservations;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence;
using MassTransit;

namespace Inventory.Infrastructure.Consumers;

/// <summary>Sabit kuyruk adı (wire contract). Retry dışta, EF outbox içte; domain hatası retry'a girmez.</summary>
public sealed class ReleaseStockConsumerDefinition : ConsumerDefinition<ReleaseStockConsumer>
{
    public ReleaseStockConsumerDefinition() => EndpointName = QueueNames.ReleaseStock;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReleaseStockConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.UseTransientDatabaseRetry(typeof(InventoryDomainException)));
        endpointConfigurator.UseEntityFrameworkOutbox<InventoryDbContext>(context);
    }
}
