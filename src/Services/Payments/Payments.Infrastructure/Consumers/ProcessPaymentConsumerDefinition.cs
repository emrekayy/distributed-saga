using BuildingBlocks.Messaging;
using MassTransit;
using Payments.Application.Transactions;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Consumers;

/// <summary>Sabit kuyruk adı (wire contract). Retry dışta, EF outbox içte; domain hatası retry'a girmez.</summary>
public sealed class ProcessPaymentConsumerDefinition : ConsumerDefinition<ProcessPaymentConsumer>
{
    /// <summary>Aynı anda işlenen mesaj üst sınırı; satır kilitleri (FOR UPDATE) sırayla bekletir (ADR-020).</summary>
    public const int ConcurrencyLimit = 16;

    public ProcessPaymentConsumerDefinition()
    {
        EndpointName = QueueNames.ProcessPayment;
        ConcurrentMessageLimit = ConcurrencyLimit;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProcessPaymentConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.UseTransientDatabaseRetry(typeof(PaymentsDomainException)));
        endpointConfigurator.UseEntityFrameworkOutbox<PaymentDbContext>(context);
    }
}
