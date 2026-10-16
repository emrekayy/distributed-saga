using Shared.Contracts.Common;

namespace Shared.Contracts.Stock.Commands;

/// <summary>
/// Siparişin kalemleri için stok ayır.
/// Publisher: Order saga (OrderSubmitted sonrası). Consumer: Stock servisi.
/// <para>
/// Ya hep ya hiç: kalemlerden biri bile yetersizse hiçbir kalem rezerve edilmez ve
/// StockReservationFailed döner; aksi halde tüm kalemler rezerve edilir ve StockReserved döner.
/// </para>
/// <para>
/// Rezervasyon OrderId ile tekildir: aynı OrderId için tekrar gelen komut ikinci rezervasyon
/// yapmaz, mevcut sonucu yeniden publish eder.
/// </para>
/// </summary>
public sealed record ReserveStock
{
    public required Guid OrderId { get; init; }

    public required IReadOnlyList<OrderItemDto> Items { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset RequestedAt { get; init; }
}
