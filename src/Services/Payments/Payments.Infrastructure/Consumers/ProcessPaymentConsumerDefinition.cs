using BuildingBlocks.Messaging;
using MassTransit;
using Payments.Application.Transactions;
using Payments.Domain;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure.Consumers;

/// <summary>Sabit kuyruk adı (wire contract). Retry dışta, EF outbox içte; domain hatası retry'a girmez.</summary>
public sealed class ProcessPaymentConsumerDefinition : ConsumerDefinition<ProcessPaymentConsumer>
{
    public ProcessPaymentConsumerDefinition() => EndpointName = QueueNames.ProcessPayment;

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ProcessPaymentConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r => r.UseTransientDatabaseRetry(typeof(PaymentsDomainException)));
        endpointConfigurator.UseEntityFrameworkOutbox<PaymentDbContext>(context);
    }
}
