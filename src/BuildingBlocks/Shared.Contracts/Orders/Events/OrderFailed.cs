namespace Shared.Contracts.Orders.Events;

/// <summary>
/// Sipariş tamamlanamadı; gerekli telafiler yapıldıktan sonra publish edilir, saga final.
/// Publisher: Order saga. Consumer: Order servisi (sipariş durumu), ileride bildirim gibi dış dinleyiciler.
/// </summary>
public sealed record OrderFailed
{
    public required Guid OrderId { get; init; }

    public required string Reason { get; init; }

    /// <summary>Hatanın kaynağındaki kod (bkz. Common.ReasonCodes), saga tarafından aynen taşınır.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
