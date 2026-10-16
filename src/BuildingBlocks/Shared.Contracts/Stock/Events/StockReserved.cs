namespace Shared.Contracts.Stock.Events;

/// <summary>
/// Siparişin tüm kalemleri rezerve edildi.
/// Publisher: Stock servisi. Consumer: Order saga (ProcessPayment gönderir).
/// </summary>
public sealed record StockReserved
{
    public required Guid OrderId { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
