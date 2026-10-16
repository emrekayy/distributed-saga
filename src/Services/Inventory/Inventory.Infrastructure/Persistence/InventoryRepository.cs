using System.Data;
using Inventory.Application.Products;
using Inventory.Application.Reservations;
using Inventory.Domain.Products;
using Inventory.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Persistence;

internal sealed class InventoryRepository(InventoryDbContext db) : IProductRepository, IReservationRepository, IUnitOfWork
{
    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Product>> GetForUpdateAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, Product>();
        }

        // ORDER BY + FOR UPDATE: Postgres satır kilitlerini sıralı çıktı sırasıyla alır → sabit kilit sırası.
        var idArray = ids.Distinct().ToArray();
        var products = await db.Products
            .FromSql($"""SELECT * FROM "Products" WHERE "Id" = ANY({idArray}) ORDER BY "Id" FOR UPDATE""")
            .AsTracking()
            .ToListAsync(cancellationToken);
        return products.ToDictionary(p => p.Id);
    }

    public Task<Reservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Reservations.FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public async Task<Reservation?> GetByOrderIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var locked = await db.Reservations
            .FromSql($"""SELECT * FROM "Reservations" WHERE "OrderId" = {orderId} FOR UPDATE""")
            .AsTracking()
            .Include(r => r.Items)
            .ToListAsync(cancellationToken);
        return locked.SingleOrDefault();
    }

    public void Add(Reservation reservation) => db.Reservations.Add(reservation);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
