using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Order.Application.Orders;
using Order.Infrastructure.Persistence;

namespace Order.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(PostgresConnection.Build(configuration, "ORDER")));

        services.AddScoped<OrderRepository>();
        services.AddScoped<IOrderRepository>(sp => sp.GetRequiredService<OrderRepository>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OrderRepository>());
        services.AddScoped<IOrderSagaStateReader>(sp => sp.GetRequiredService<OrderRepository>());

        services.AddMessaging<OrderDbContext>(configuration);

        // MassTransit kendi bus health check'ini "ready" tag'iyle ekler.
        services.AddHealthChecks().AddDbContextCheck<OrderDbContext>("order-db", tags: ["ready"]);

        return services;
    }

    /// <summary>Yalnızca Development'ta çağrılır; production'da migration ayrı bir adımdır.</summary>
    public static async Task MigrateOrderDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await db.Database.MigrateAsync();
    }
}
