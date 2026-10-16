using BuildingBlocks.Messaging;
using Inventory.Application.Reservations;
using Inventory.Domain;
using Inventory.Infrastructure.Persistence;
using MassTransit;

namespace Inventory.Infrastructure.Consumers;

/// <summary>Sabit kuyruk adı (wire contract). Retry dışta, EF outbox içte; domain hatası retry'a girmez.</summary>
public sealed class ReserveStockConsumerDefinition : ConsumerDefinition<ReserveStockConsumer>
{
    /// <summary>Aynı anda işlenen mesaj üst sınırı; satır kilitleri (FOR UPDATE) sırayla bekletir (ADR-020).</summary>
    public const int ConcurrencyLimit = 16;

    public ReserveStockConsumerDefinition()
    {
        EndpointName = QueueNames.ReserveStock;
        ConcurrentMessageLimit = ConcurrencyLimit;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ReserveStockConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.UseTransientDatabaseRetry(typeof(InventoryDomainException)));
        endpointConfigurator.UseEntityFrameworkOutbox<InventoryDbContext>(context);
    }
}
