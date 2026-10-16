using Ordering.Application.Orders;
using Ordering.Domain.Orders;

namespace Ordering.UnitTests.Fakes;

/// <summary>IOrderRepository + IUnitOfWork için bellek içi fake; eşzamanlı key yarışını simüle edebilir.</summary>
public sealed class InMemoryOrderStore : IOrderRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Order> _committed = [];
    private readonly List<Order> _pending = [];

    /// <summary>Ayarlanırsa bir sonraki SaveChanges, bu siparişin "eşzamanlı istekte" kazandığını simüle eder.</summary>
    public Order? ConcurrentWinner { get; set; }

    public int SaveCount { get; private set; }

    public IReadOnlyCollection<Order> Committed => _committed.Values;

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_committed.GetValueOrDefault(id));

    public Task<Order?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_committed.Values.FirstOrDefault(o => o.IdempotencyKey == idempotencyKey));

    public void Add(Order order) => _pending.Add(order);

    public void Seed(Order order) => _committed[order.Id] = order;

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        if (ConcurrentWinner is { } winner)
        {
            ConcurrentWinner = null;
            _pending.Clear();
            _committed[winner.Id] = winner;
            throw new DuplicateIdempotencyKeyException(winner.IdempotencyKey, new InvalidOperationException("23505"));
        }

        foreach (var order in _pending)
        {
            _committed[order.Id] = order;
        }

        _pending.Clear();
        return Task.CompletedTask;
    }
}
