using System.Data;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Configuration;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Transactions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payments.Application.Wallets;
using Payments.Infrastructure.Consumers;
using Payments.Infrastructure.FaultInjection;
using Payments.Infrastructure.Persistence;

namespace Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
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

        AddFaultInjection(services, environment);

        services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>("payment-db", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Seçenekler her ortamda bağlanır ve doğrulanır (Production'da Enabled=true startup'ı durdurur); enjeksiyon filtresi
    /// ise yalnızca Development/Testing'de kaydedilir, yani Production'da kod yolu hiç kurulmaz.
    /// </summary>
    private static void AddFaultInjection(IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOptions<FaultInjectionOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                configuration.GetSection(FaultInjectionOptions.SectionName).Bind(options))
            .ValidateOnStart();
        services.AddSingleton<IOptionsChangeTokenSource<FaultInjectionOptions>>(sp =>
            new ConfigurationChangeTokenSource<FaultInjectionOptions>(
                sp.GetRequiredService<IConfiguration>().GetSection(FaultInjectionOptions.SectionName)));
        services.AddSingleton<IValidateOptions<FaultInjectionOptions>>(
            sp => new FaultInjectionOptionsValidator(sp.GetRequiredService<IHostEnvironment>()));

        if (FaultInjectionOptionsValidator.IsAllowed(environment))
        {
            services.AddSingleton<FaultInjectionFilter>();
        }
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
