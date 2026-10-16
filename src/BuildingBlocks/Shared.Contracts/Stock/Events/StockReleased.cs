namespace Shared.Contracts.Stock.Events;

/// <summary>
/// Siparişin rezervasyonu serbest bırakıldı (rezervasyon yoksa veya zaten bırakılmışsa da gönderilir).
/// Publisher: Stock servisi. Consumer: Order saga (OrderFailed publish eder).
/// </summary>
public sealed record StockReleased
{
    public required Guid OrderId { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
