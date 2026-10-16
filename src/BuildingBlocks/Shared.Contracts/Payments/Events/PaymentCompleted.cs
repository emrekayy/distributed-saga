namespace Shared.Contracts.Payments.Events;

/// <summary>
/// Tahsilat başarılı.
/// Publisher: Payment servisi. Consumer: Order saga (OrderCompleted publish eder).
/// </summary>
public sealed record PaymentCompleted
{
    public required Guid OrderId { get; init; }

    /// <summary>Payment servisinin kendi ödeme kimliği (iade ve raporlama için).</summary>
    public required Guid PaymentId { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>ISO 4217 para birimi kodu.</summary>
    public required string Currency { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
