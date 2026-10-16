namespace Shared.Contracts.Orders.Events;

/// <summary>
/// Stok rezerve edildi ve ödeme alındı; saga final.
/// Publisher: Order saga. Consumer: Order servisi (sipariş durumu), ileride bildirim gibi dış dinleyiciler.
/// </summary>
public sealed record OrderCompleted
{
    public required Guid OrderId { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
