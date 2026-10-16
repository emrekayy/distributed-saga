using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Saga;
using Ordering.Domain.Orders;

namespace Ordering.Infrastructure.Persistence;

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
