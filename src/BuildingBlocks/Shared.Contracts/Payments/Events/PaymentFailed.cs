namespace Shared.Contracts.Payments.Events;

/// <summary>
/// Tahsilat yapılamadı; para çekilmedi.
/// Publisher: Payment servisi. Consumer: Order saga (compensation olarak ReleaseStock gönderir).
/// </summary>
public sealed record PaymentFailed
{
    public required Guid OrderId { get; init; }

    public required string Reason { get; init; }

    /// <summary>PAYMENT_DECLINED, PAYMENT_ERROR veya VALIDATION_ERROR.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
