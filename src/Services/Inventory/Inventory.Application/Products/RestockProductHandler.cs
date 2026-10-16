using Inventory.Domain.Products;

namespace Inventory.Application.Products;

/// <summary>Demo/admin: stok ekler. Satırı kilitleyerek eşzamanlı rezervasyonla yarışmaz.</summary>
public sealed class RestockProductHandler(IProductRepository products, IUnitOfWork unitOfWork)
{
    /// <summary>Ürün yoksa null döner.</summary>
    public async Task<Product?> HandleAsync(Guid productId, int quantity, CancellationToken cancellationToken)
    {
        Product? restocked = null;
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var locked = await products.GetForUpdateAsync([productId], ct);
            if (!locked.TryGetValue(productId, out var product))
            {
                return;
            }

            product.Restock(quantity);
            await unitOfWork.SaveChangesAsync(ct);
            restocked = product;
        }, cancellationToken);

        return restocked;
    }
}
