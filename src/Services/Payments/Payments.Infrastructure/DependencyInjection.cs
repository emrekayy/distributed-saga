using System.Data;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Transactions;
using Payments.Application.Wallets;
using Payments.Infrastructure.Consumers;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>((sp, o) =>
            o.UseNpgsql(PostgresConnection.Build(sp.GetRequiredService<IConfiguration>(), "PAYMENT")));

        services.AddScoped<PaymentRepository>();
        services.AddScoped<IPaymentRepository>(sp => sp.GetRequiredService<PaymentRepository>());
        services.AddScoped<IWalletRepository>(sp => sp.GetRequiredService<PaymentRepository>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentRepository>());

        // Pessimistic (FOR UPDATE) çalıştığımız için outbox transaction'ı ReadCommitted (ADR-020).
        services.AddMessaging<PaymentDbContext>(
            configuration,
            configureRegistrations: x =>
            {
                x.AddConsumer<ProcessPaymentConsumer, ProcessPaymentConsumerDefinition>();
                x.AddConsumer<RefundPaymentConsumer, RefundPaymentConsumerDefinition>();
            },
            outboxIsolationLevel: IsolationLevel.ReadCommitted);

        services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>("payment-db", tags: ["ready"]);

        return services;
    }

    /// <summary>Yalnızca Development'ta çağrılır; production'da migration ayrı bir adımdır.</summary>
    public static async Task MigrateAndSeedPaymentDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        await db.Database.MigrateAsync();
        await PaymentSeeder.SeedAsync(db);
    }
}
