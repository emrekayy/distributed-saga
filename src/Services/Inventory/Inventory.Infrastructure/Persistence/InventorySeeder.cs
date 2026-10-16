using Inventory.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Persistence;

/// <summary>Development demo verisi. Id'ler sabittir (README'de listelenir); yalnızca eksik ürünler eklenir.</summary>
public static class InventorySeeder
{
    public static readonly Guid Keyboard = new("a1a1a1a1-0000-0000-0000-000000000001");
    public static readonly Guid Mouse = new("a1a1a1a1-0000-0000-0000-000000000002");
    public static readonly Guid OutOfStockMonitor = new("a1a1a1a1-0000-0000-0000-000000000003");
    public static readonly Guid Headset = new("a1a1a1a1-0000-0000-0000-000000000004");

    public static async Task SeedAsync(InventoryDbContext db, CancellationToken cancellationToken = default)
    {
        Product[] seed =
        [
            new(Keyboard, "Mechanical Keyboard", 100),
            new(Mouse, "Wireless Mouse", 50),
            new(OutOfStockMonitor, "4K Monitor (out of stock)", 0),
            new(Headset, "Headset (limited)", 10),
        ];

        var existing = await db.Products.Select(p => p.Id).ToListAsync(cancellationToken);
        db.Products.AddRange(seed.Where(p => !existing.Contains(p.Id)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
