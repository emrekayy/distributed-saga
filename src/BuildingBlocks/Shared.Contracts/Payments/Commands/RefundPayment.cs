namespace Shared.Contracts.Payments.Commands;

/// <summary>
/// Compensation: siparişin tahsilatını tam iade et. Şimdilik yalnızca sözleşme; ödeme sonrası
/// bir adım eklendiğinde saga kullanacak.
/// Publisher: Order saga. Consumer: Payment servisi.
/// <para>
/// İş açısından başarısız olamaz; yalnızca teknik hatalarda retry edilir. Tahsilat yoksa ya da
/// zaten iade edildiyse de PaymentRefunded publish edilir, aksi halde saga takılır.
/// Bu yüzden "refund failed" event'i yoktur.
/// </para>
/// </summary>
public sealed record RefundPayment
{
    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>ISO 4217 para birimi kodu.</summary>
    public required string Currency { get; init; }

    public required string Reason { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset RequestedAt { get; init; }
}
