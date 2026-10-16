namespace Shared.Contracts.Common;

/// <summary>
/// Sipariş kalemi. OrderSubmitted ve ReserveStock içinde taşınır.
/// </summary>
public sealed record OrderItemDto
{
    public required Guid ProductId { get; init; }

    /// <summary>Pozitif adet.</summary>
    public required int Quantity { get; init; }

    /// <summary>Birim fiyat; para birimi kalemi taşıyan mesajın Currency alanındadır.</summary>
    public required decimal UnitPrice { get; init; }
}
