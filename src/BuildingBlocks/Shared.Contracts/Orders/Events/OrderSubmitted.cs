using Shared.Contracts.Common;

namespace Shared.Contracts.Orders.Events;

/// <summary>
/// Yeni sipariş oluşturuldu; saga'yı başlatır.
/// Publisher: Order servisi (transactional outbox). Consumer: Order saga.
/// </summary>
public sealed record OrderSubmitted
{
    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required IReadOnlyList<OrderItemDto> Items { get; init; }

    /// <summary>Order domain'inin hesapladığı toplam; saga yeniden hesaplamaz.</summary>
    public required decimal TotalAmount { get; init; }

    /// <summary>ISO 4217 para birimi kodu (örn. "TRY").</summary>
    public required string Currency { get; init; }

    /// <summary>UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }
}
