using Microsoft.EntityFrameworkCore;
using Npgsql;
using Order.Application.Orders;
using Order.Infrastructure.Persistence.Configurations;

namespace Order.Infrastructure.Persistence;

using Order.Domain.Orders;

internal sealed class OrderRepository(OrderDbContext db) : IOrderRepository, IUnitOfWork, IOrderSagaStateReader
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OrderConfiguration.IdempotencyKeyIndex,
        })
        {
            var key = ex.Entries.Select(e => e.Entity).OfType<Order>().FirstOrDefault()?.IdempotencyKey ?? "?";

            // Kaybeden isteğin sipariş + outbox satırları atılır; kazananın kaydı okunabilsin.
            db.ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException(key, ex);
        }
    }

    public Task<string?> GetCurrentStateAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.OrderStates.AsNoTracking()
            .Where(s => s.CorrelationId == orderId)
            .Select(s => s.CurrentState)
            .FirstOrDefaultAsync(cancellationToken);
}
