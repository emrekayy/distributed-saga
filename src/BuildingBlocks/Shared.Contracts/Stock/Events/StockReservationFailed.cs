namespace Shared.Contracts.Stock.Events;

/// <summary>
/// Rezervasyon yapılamadı; hiçbir kalem rezerve edilmedi, telafi gerekmez.
/// Publisher: Stock servisi. Consumer: Order saga (OrderFailed publish eder).
/// </summary>
public sealed record StockReservationFailed
{
    public required Guid OrderId { get; init; }

    public required string Reason { get; init; }

    /// <summary>INSUFFICIENT_STOCK, PRODUCT_NOT_FOUND veya VALIDATION_ERROR.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
