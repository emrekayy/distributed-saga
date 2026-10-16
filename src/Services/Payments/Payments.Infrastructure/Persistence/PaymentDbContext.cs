using BuildingBlocks.Messaging.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Payments.Domain.Transactions;
using Payments.Domain.Wallets;

namespace Payments.Infrastructure.Persistence;

/// <summary>
/// Payments servisinin tek DbContext'i: cüzdanlar, ödemeler ve MassTransit inbox/outbox tabloları.
/// Bakiye değişimi, ödeme kaydı ve outbox mesajları aynı SaveChanges → tek transaction'da yazılır.
/// </summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseMoneyPrecision();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
