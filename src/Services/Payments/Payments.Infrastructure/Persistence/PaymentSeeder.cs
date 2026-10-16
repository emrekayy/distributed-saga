using Microsoft.EntityFrameworkCore;
using Payments.Domain.Wallets;

namespace Payments.Infrastructure.Persistence;

/// <summary>Development demo verisi. Id'ler sabittir (README'de listelenir); yalnızca eksik cüzdanlar eklenir.</summary>
public static class PaymentSeeder
{
    /// <summary>Ordering README örneğindeki müşteri.</summary>
    public static readonly Guid RichCustomer = new("6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b");
    public static readonly Guid PoorCustomer = new("b2b2b2b2-0000-0000-0000-000000000002");

    public static async Task SeedAsync(PaymentDbContext db, CancellationToken cancellationToken = default)
    {
        Wallet[] seed =
        [
            new(RichCustomer, "TRY", 1_000_000m),
            new(PoorCustomer, "TRY", 100m),
        ];

        var existing = await db.Wallets.Select(w => w.CustomerId).ToListAsync(cancellationToken);
        db.Wallets.AddRange(seed.Where(w => !existing.Contains(w.CustomerId)));
        await db.SaveChangesAsync(cancellationToken);
    }
}
