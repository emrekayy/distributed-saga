using Inventory.Application.Products;
using Inventory.Application.Reservations;
using Inventory.Domain.Products;
using Inventory.Domain.Reservations;

namespace Inventory.UnitTests.Fakes;

/// <summary>Repository arayüzlerinin bellek içi karşılığı; "kilit" gerçek DB testlerinde (Adım 5) doğrulanır.</summary>
public sealed class InMemoryInventoryStore : IProductRepository, IReservationRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Product> _products = [];
    private readonly Dictionary<Guid, Reservation> _reservations = [];

    public int SaveCount { get; private set; }

    public Product AddProduct(Guid id, string name, int quantity)
    {
        var product = new Product(id, name, quantity);
        _products[id] = product;
        return product;
    }

    public Product Product(Guid id) => _products[id];

    public Reservation? Reservation(Guid orderId) => _reservations.GetValueOrDefault(orderId);

    public Reservation SeedReservation(Reservation reservation)
    {
        _reservations[reservation.OrderId] = reservation;
        return reservation;
    }

    public Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(_products.Values.ToList());

    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_products.GetValueOrDefault(id));

    public Task<IReadOnlyDictionary<Guid, Product>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Product>>(
            ids.Where(_products.ContainsKey).ToDictionary(id => id, id => _products[id]));

    public Task<Reservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(_reservations.GetValueOrDefault(orderId));

    public Task<Reservation?> GetByOrderIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
        GetByOrderIdAsync(orderId, cancellationToken);

    public void Add(Reservation reservation) => _reservations.Add(reservation.OrderId, reservation);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        action(cancellationToken);
}
