using Inventory.Domain.Products;

namespace Inventory.Application.Products;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);

    Task<Product?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Ürün satırlarını <b>ProductId sırasıyla</b> <c>SELECT … FOR UPDATE</c> ile kilitler (sabit kilit sırası
    /// deadlock'u önler, ADR-020). Kilit, çağıranın açık transaction'ı bitene kadar tutulur. Bulunamayan Id'ler
    /// sonuçta yer almaz.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Product>> GetForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
