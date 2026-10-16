using System.Data;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Configuration;
using Inventory.Application.Products;
using Inventory.Application.Reservations;
using Inventory.Infrastructure.Consumers;
using Inventory.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<InventoryDbContext>((sp, o) =>
            o.UseNpgsql(PostgresConnection.Build(sp.GetRequiredService<IConfiguration>(), "STOCK")));

        services.AddScoped<InventoryRepository>();
        services.AddScoped<IProductRepository>(sp => sp.GetRequiredService<InventoryRepository>());
        services.AddScoped<IReservationRepository>(sp => sp.GetRequiredService<InventoryRepository>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InventoryRepository>());

        // Pessimistic (FOR UPDATE) çalıştığımız için outbox transaction'ı ReadCommitted (ADR-020).
        services.AddMessaging<InventoryDbContext>(
            configuration,
            configureRegistrations: x =>
            {
                x.AddConsumer<ReserveStockConsumer, ReserveStockConsumerDefinition>();
                x.AddConsumer<ReleaseStockConsumer, ReleaseStockConsumerDefinition>();
            },
            outboxIsolationLevel: IsolationLevel.ReadCommitted);

        services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("stock-db", tags: ["ready"]);

        return services;
    }

    /// <summary>Yalnızca Development'ta çağrılır; production'da migration ayrı bir adımdır.</summary>
    public static async Task MigrateInventoryDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>Demo verisi; yalnızca <c>Seed:Enabled</c> açıkken çağrılır. İdempotent.</summary>
    public static async Task SeedInventoryDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await InventorySeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>());
    }
}
