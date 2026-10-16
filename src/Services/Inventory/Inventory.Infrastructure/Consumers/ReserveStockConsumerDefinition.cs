using BuildingBlocks.Messaging;
using Inventory.Application.Reservations;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence;
using MassTransit;

namespace Inventory.Infrastructure.Consumers;

/// <summary>Sabit kuyruk adı (wire contract). Retry dışta, EF outbox içte; domain hatası retry'a girmez.</summary>
public sealed class ReserveStockConsumerDefinition : ConsumerDefinition<ReserveStockConsumer>
{
    public ReserveStockConsumerDefinition() => EndpointName = QueueNames.ReserveStock;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReserveStockConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.UseTransientDatabaseRetry(typeof(InventoryDomainException)));
        endpointConfigurator.UseEntityFrameworkOutbox<InventoryDbContext>(context);
    }
}
