using MassTransit;
using Microsoft.EntityFrameworkCore;
using Order.Application.Saga;

namespace Order.Infrastructure.Persistence;

using Order.Domain.Orders;

/// <summary>
/// Order servisinin tek DbContext'i: Order aggregate, saga state ve MassTransit inbox/outbox tabloları.
/// Saga, aggregate güncellemesi ve outbox mesajları aynı SaveChanges → tek transaction'da yazılır.
/// </summary>
public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderState> OrderStates => Set<OrderState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderDbContext).Assembly);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
