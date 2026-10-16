using Inventory.Application.Products;
using Inventory.Domain.Reservations;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Stock.Commands;

namespace Inventory.Application.Reservations;

/// <summary>
/// İş anahtarı OrderId: kayıt varsa işlem tekrarlanmaz, kayıtlı sonuç (başarılı ya da başarısız) yeniden
/// yayınlanır. Yoksa ürün satırları sıralı kilitlenir, karar verilir, sonuç kaydedilir; event outbox
/// üzerinden aynı SaveChanges'te yazılır. Aynı OrderId ile eşzamanlı gelen ikinci insert 23505 alır ve
/// retry'da kayıtlı sonucu bulur.
/// </summary>
public sealed class ReserveStockConsumer(
    IReservationRepository reservations,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReserveStockConsumer> logger) : IConsumer<ReserveStock>
{
    public async Task Consume(ConsumeContext<ReserveStock> context)
    {
        var command = context.Message;
        var cancellationToken = context.CancellationToken;

        var reservation = await reservations.GetByOrderIdAsync(command.OrderId, cancellationToken);
        if (reservation is null)
        {
            var items = command.Items.Select(i => new ReservationItem(i.ProductId, i.Quantity)).ToList();
            var lockedProducts = await products.GetForUpdateAsync(
                items.Select(i => i.ProductId).Distinct().ToList(), cancellationToken);

            reservation = Reservation.Create(command.OrderId, items, lockedProducts, clock.GetUtcNow());
            reservations.Add(reservation);
            logger.LogInformation(
                "Reservation for order {OrderId} recorded as {Status} ({Reason})",
                command.OrderId, reservation.Status, reservation.Rejection);
        }
        else
        {
            logger.LogInformation(
                "Duplicate ReserveStock for order {OrderId}; republishing recorded result {Status}",
                command.OrderId, reservation.Status);
        }

        await ReservationResultPublisher.PublishAsync(context, reservation, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
