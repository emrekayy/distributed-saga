using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Order.Application.Orders;
using Order.Application.Saga;
using Order.Infrastructure.Persistence;
using Order.Infrastructure.Saga;
using Quartz;

namespace Order.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = PostgresConnection.Build(configuration, "ORDER");
        services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(connectionString));

        services.AddOptions<OrderSagaOptions>()
            .Bind(configuration.GetSection(OrderSagaOptions.SectionName))
            .Validate(o => o.PaymentTimeout > TimeSpan.Zero, "OrderSaga:PaymentTimeout must be positive.")
            .ValidateOnStart();

        AddQuartzScheduler(services, connectionString);

        services.AddScoped<OrderRepository>();
        services.AddScoped<IOrderRepository>(sp => sp.GetRequiredService<OrderRepository>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderRepository>());
        services.AddScoped<IOrderSagaStateReader>(sp => sp.GetRequiredService<OrderRepository>());

        services.AddMessaging<OrderDbContext>(
            configuration,
            configureRegistrations: x =>
            {
                // Saga Schedule(): mesaj Quartz'a publish edilir, Quartz zamanı gelince saga'ya geri gönderir.
                x.AddPublishMessageScheduler();
                x.AddQuartzConsumers();

                // Saga state, Order aggregate ve outbox aynı scoped OrderDbContext'i paylaşır → tek transaction.
                x.AddSagaStateMachine<OrderStateMachine, OrderState, OrderStateDefinition>()
                    .EntityFrameworkRepository(r =>
                    {
                        r.ConcurrencyMode = ConcurrencyMode.Optimistic; // xmin
                        r.ExistingDbContext<OrderDbContext>();
                        r.UsePostgres();
                    });
            },
            configureBus: (_, bus) => bus.UsePublishMessageScheduler());

        // MassTransit kendi bus health check'ini "ready" tag'iyle ekler.
        services.AddHealthChecks().AddDbContextCheck<OrderDbContext>("order-db", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Quartz in-process, job'lar order_db'deki "quartz" şemasında (ADO job store). Restart'ta bekleyen
    /// timeout'lar kaybolmaz; clustering birden fazla API instance'ının aynı job'u iki kez çalıştırmasını önler.
    /// </summary>
    private static void AddQuartzScheduler(IServiceCollection services, string connectionString)
    {
        services.AddQuartz(q =>
        {
            q.SchedulerName = "order-service";
            q.SchedulerId = "AUTO";
            q.UsePersistentStore(store =>
            {
                store.UseProperties = true;
                store.UsePostgres(p =>
                {
                    p.ConnectionString = connectionString;
                    p.TablePrefix = QuartzSchema.TablePrefix;
                });
                store.UseSystemTextJsonSerializer();
                store.UseClustering();
            });
        });
        services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
    }

    /// <summary>Yalnızca Development'ta çağrılır; production'da migration ayrı bir adımdır.</summary>
    public static async Task MigrateOrderDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await db.Database.MigrateAsync();
    }
}
