using MassTransit;
using Shared.Contracts.Common;

namespace Ordering.Application.Saga;

/// <summary>
/// Sipariş saga'sının kalıcı durumu (OrderState tablosu). CorrelationId = OrderId.
/// Kayıt final durumlarda da silinmez (denetim izi).
/// </summary>
public sealed class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = null!;

    public Guid CustomerId { get; set; }

    /// <summary>ReserveStock için gerekli kalemler (jsonb). Saga Order tablosundan okumaz.</summary>
    public List<OrderItemDto> Items { get; set; } = [];

    /// <summary>OrderSubmitted.TotalAmount; ProcessPayment.Amount buradan gelir, yeniden hesaplanmaz.</summary>
    public decimal TotalAmount { get; set; }

    public string Currency { get; set; } = null!;

    /// <summary>Compensation sonrası OrderFailed'da kullanılacak sebep (PaymentFailed veya timeout).</summary>
    public string? FailureReasonCode { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Schedule edilen PaymentTimeoutExpired'ın token'ı (iptal için).</summary>
    public Guid? PaymentTimeoutTokenId { get; set; }

    /// <summary>Timeout sonrası geç gelen PaymentCompleted için RefundPayment gönderildi mi.</summary>
    public DateTimeOffset? RefundRequestedAt { get; set; }

    public DateTimeOffset? RefundedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token; Postgres xmin sistem kolonuna eşlenir.</summary>
    public uint RowVersion { get; set; }
}
