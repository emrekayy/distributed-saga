namespace Shared.Contracts.Stock.Commands;

/// <summary>
/// Compensation: siparişin stok rezervasyonunu serbest bırak.
/// Publisher: Order saga (PaymentFailed sonrası). Consumer: Stock servisi.
/// <para>
/// İş açısından başarısız olamaz; yalnızca teknik hatalarda retry edilir. Rezervasyon yoksa ya da
/// zaten serbest bırakılmışsa da StockReleased publish edilir, aksi halde saga takılır.
/// Bu yüzden "release failed" event'i yoktur.
/// </para>
/// </summary>
public sealed record ReleaseStock
{
    public required Guid OrderId { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset RequestedAt { get; init; }
}
