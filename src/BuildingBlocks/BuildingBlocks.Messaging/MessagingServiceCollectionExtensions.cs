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
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureRegistrations = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureBus = null)
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
            });

            configureRegistrations?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
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
