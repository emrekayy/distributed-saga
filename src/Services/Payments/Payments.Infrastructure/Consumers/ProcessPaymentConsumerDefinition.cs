using BuildingBlocks.Messaging;
using MassTransit;
using Payments.Application.Transactions;
using Payments.Domain;
using Microsoft.Extensions.DependencyInjection;
using Payments.Infrastructure.FaultInjection;
using Payments.Infrastructure.Persistence;
using Shared.Contracts.Payments.Commands;

namespace Payments.Infrastructure.Consumers;

/// <summary>Sabit kuyruk adı (wire contract). Retry dışta, EF outbox içte (opsiyonel hata enjeksiyonu consumer düzeyinde); domain hatası retry'a girmez.</summary>
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

        // Consumer düzeyinde (retry ve outbox'ın içinde): enjekte hata gerçek bir consumer hatası gibi retry'a takılır,
        // transaction geri alınır. Yalnızca Development/Testing'de kayıtlıdır; Production'da bu blok hiçbir şey eklemez.
        if (context.GetService<FaultInjectionFilter>() is { } faultInjection)
        {
            consumerConfigurator.Message<ProcessPayment>(m => m.UseFilter(faultInjection));
        }
    }
}
