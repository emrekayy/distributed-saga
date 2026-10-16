namespace Order.Application.Orders;

// Kök namespace "Order" ile tip adı "Order" çakışır; using namespace içinde olmalı (bkz. CLAUDE.md).
using Order.Domain.Orders;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    void Add(Order order);
}
