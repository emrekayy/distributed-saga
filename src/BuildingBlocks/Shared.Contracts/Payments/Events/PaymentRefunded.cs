namespace Shared.Contracts.Payments.Events;

/// <summary>
/// İade tamamlandı (tahsilat yoksa veya zaten iade edildiyse de gönderilir). Şimdilik yalnızca sözleşme.
/// Publisher: Payment servisi. Consumer: Order saga.
/// </summary>
public sealed record PaymentRefunded
{
    public required Guid OrderId { get; init; }

    /// <summary>Fiilen iade edilen tutar; tahsilat hiç yapılmadıysa 0.</summary>
    public required decimal Amount { get; init; }

    /// <summary>ISO 4217 para birimi kodu.</summary>
    public required string Currency { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
