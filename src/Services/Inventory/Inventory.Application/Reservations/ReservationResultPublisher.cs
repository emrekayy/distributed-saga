using Inventory.Domain.Reservations;
using MassTransit;
using Shared.Contracts.Common;
using Shared.Contracts.Stock.Events;

namespace Inventory.Application.Reservations;

internal static class ReservationResultPublisher
{
    /// <summary>
    /// Kayıtlı ReserveStock sonucunu yayınlar. Released bir rezervasyonun asıl sonucu da başarıydı;
    /// saga Compensating/Failed state'inde bu event'i yok sayar (ignore matrisi).
    /// </summary>
    public static Task PublishAsync(ConsumeContext context, Reservation reservation, DateTimeOffset now) =>
        reservation.Status == ReservationStatus.Rejected
            ? context.Publish(new StockReservationFailed
            {
                OrderId = reservation.OrderId,
                Reason = reservation.Reason ?? "Stock reservation was rejected.",
                ReasonCode = ToReasonCode(reservation.Rejection),
                OccurredAt = now,
            })
            : context.Publish(new StockReserved { OrderId = reservation.OrderId, OccurredAt = now });

    private static string ToReasonCode(RejectionReason? reason) => reason switch
    {
        RejectionReason.ProductNotFound => ReasonCodes.ProductNotFound,
        RejectionReason.InsufficientStock => ReasonCodes.InsufficientStock,
        _ => ReasonCodes.ValidationError,
    };
}
