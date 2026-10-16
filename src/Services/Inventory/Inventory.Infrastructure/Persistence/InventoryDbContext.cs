using BuildingBlocks.Messaging.Persistence;
using Inventory.Domain.Products;
using Inventory.Domain.Reservations;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory servisinin tek DbContext'i: ürünler, rezervasyonlar ve MassTransit inbox/outbox tabloları.
/// Stok değişimi, rezervasyon kaydı ve outbox mesajları aynı SaveChanges → tek transaction'da yazılır.
/// </summary>
public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.UseMoneyPrecision();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
