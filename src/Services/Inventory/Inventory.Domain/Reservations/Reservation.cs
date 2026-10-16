using Inventory.Domain.Products;

namespace Inventory.Domain.Reservations;

/// <summary>
/// Sipariş başına tek rezervasyon (PK = OrderId, iş anahtarı). Başarısız sonuçlar da (Rejected) kaydedilir;
/// aynı OrderId ile tekrar gelen komut işlemi tekrarlamaz, kayıtlı sonucu yeniden yayınlar.
/// </summary>
public sealed class Reservation
{
    private readonly List<ReservationItem> _items = [];

    private Reservation()
    {
    }

    public Guid OrderId { get; private set; }

    public ReservationStatus Status { get; private set; }

    public RejectionReason? Rejection { get; private set; }

    public string? Reason { get; private set; }

    public IReadOnlyList<ReservationItem> Items => _items;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Ya hep ya hiç: tüm kalemler karşılanabiliyorsa hepsi düşülür (Reserved), aksi halde hiçbir ürüne
    /// dokunulmaz (Rejected). <paramref name="lockedProducts"/> çağıranın kilitlediği (FOR UPDATE) satırlardır.
    /// Aynı ürünün birden çok kalemi birleştirilir.
    /// </summary>
    public static Reservation Create(
        Guid orderId,
        IEnumerable<ReservationItem> requestedItems,
        IReadOnlyDictionary<Guid, Product> lockedProducts,
        DateTimeOffset now)
    {
        var items = requestedItems.ToList();
        if (items.Count == 0 || items.Any(i => i.Quantity <= 0))
        {
            return Build(orderId, items, ReservationStatus.Rejected, RejectionReason.ValidationError,
                "Reservation needs at least one item and every quantity must be greater than zero.", now);
        }

        var combined = items
            .GroupBy(i => i.ProductId)
            .Select(g => new ReservationItem(g.Key, g.Sum(i => i.Quantity)))
            .OrderBy(i => i.ProductId)
            .ToList();

        foreach (var item in combined)
        {
            if (!lockedProducts.TryGetValue(item.ProductId, out _))
            {
                return Build(orderId, items, ReservationStatus.Rejected, RejectionReason.ProductNotFound,
                    $"Product {item.ProductId} was not found.", now);
            }
        }

        foreach (var item in combined)
        {
            var product = lockedProducts[item.ProductId];
            if (!product.CanFulfil(item.Quantity))
            {
                return Build(orderId, items, ReservationStatus.Rejected, RejectionReason.InsufficientStock,
                    $"Insufficient stock for product {item.ProductId}: requested {item.Quantity}, available {product.AvailableQuantity}.",
                    now);
            }
        }

        foreach (var item in combined)
        {
            lockedProducts[item.ProductId].Take(item.Quantity);
        }

        return Build(orderId, combined, ReservationStatus.Reserved, null, null, now);
    }

    /// <summary>
    /// Yalnızca Reserved ise stoğu iade eder ve Released yapar. Rejected ya da zaten Released ise hiçbir şeyi
    /// değiştirmez. İade edildiyse true döner. <paramref name="lockedProducts"/> kilitli satırlardır.
    /// </summary>
    public bool Release(IReadOnlyDictionary<Guid, Product> lockedProducts, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Reserved)
        {
            return false;
        }

        foreach (var item in _items)
        {
            // Ürün satırı silinmez; yine de kayıp bir satır compensation'ı bloke etmesin diye atlanır.
            if (lockedProducts.TryGetValue(item.ProductId, out var product))
            {
                product.Restock(item.Quantity);
            }
        }

        Status = ReservationStatus.Released;
        UpdatedAt = now;
        return true;
    }

    private static Reservation Build(
        Guid orderId,
        IEnumerable<ReservationItem> items,
        ReservationStatus status,
        RejectionReason? rejection,
        string? reason,
        DateTimeOffset now)
    {
        var reservation = new Reservation
        {
            OrderId = orderId,
            Status = status,
            Rejection = rejection,
            Reason = reason,
            CreatedAt = now,
            UpdatedAt = now,
        };
        reservation._items.AddRange(items);
        return reservation;
    }
}
