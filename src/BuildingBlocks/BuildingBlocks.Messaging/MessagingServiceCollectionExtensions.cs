using System.Data;
using BuildingBlocks.Messaging.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Servislerin ortak MassTransit kurulumu: RabbitMQ (.env anahtarları), kebab-case endpoint adları,
    /// EF Core transactional outbox (Postgres) + bus outbox ve command endpoint eşlemeleri.
    /// </summary>
    /// <param name="outboxIsolationLevel">
    /// Outbox'ın açtığı transaction'ın izolasyonu; null ise MassTransit varsayılanı (RepeatableRead) kalır.
    /// <c>SELECT … FOR UPDATE</c> ile pessimistic kilitleyen servisler <see cref="IsolationLevel.ReadCommitted"/>
    /// vermelidir: RepeatableRead'de kilidi bekleyen transaction, kilit açılınca 40001 serialization hatası alır (ADR-020).
    /// </param>
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureRegistrations = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureBus = null,
        IsolationLevel? outboxIsolationLevel = null)
        where TDbContext : DbContext
    {
        CommandEndpoints.Map();

        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();

            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();

                if (outboxIsolationLevel is { } isolationLevel)
                {
                    o.IsolationLevel = isolationLevel;
                }
            });

            configureRegistrations?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                var configuration = context.GetRequiredService<IConfiguration>();
                cfg.Host(
                    configuration["RABBITMQ_HOST"] ?? "localhost",
                    configuration.GetValue<ushort>("RABBITMQ_AMQP_PORT", 5672),
                    configuration["RABBITMQ_VHOST"] ?? "/",
                    h =>
                    {
                        h.Username(configuration.GetRequired("RABBITMQ_USER"));
                        h.Password(configuration.GetRequired("RABBITMQ_PASSWORD"));
                    });

                configureBus?.Invoke(context, cfg);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
