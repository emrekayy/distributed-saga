namespace Shared.Contracts.Payments.Commands;

/// <summary>
/// Sipariş tutarını tahsil et.
/// Publisher: Order saga (StockReserved sonrası). Consumer: Payment servisi.
/// <para>
/// Sipariş başına tek tahsilat: OrderId Payment tarafında unique'tir. Aynı OrderId için tekrar
/// gelen komut (yeni MessageId ile olsa bile) yeniden tahsilat yapmaz, mevcut sonucu yeniden publish eder.
/// </para>
/// </summary>
public sealed record ProcessPayment
{
    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required decimal Amount { get; init; }

    /// <summary>ISO 4217 para birimi kodu.</summary>
    public required string Currency { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset RequestedAt { get; init; }
}
