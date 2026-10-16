using Inventory.Application.Products;
using Inventory.Domain.Reservations;
using MassTransit;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace Inventory.Application.Reservations;

/// <summary>
/// Compensation: iş açısından başarısız olamaz. Reserved ise stok iade edilir ve Released yapılır; kayıt yoksa,
/// Rejected ya da zaten Released ise hiçbir şey değişmez. Her durumda StockReleased yayınlanır, aksi halde saga takılır.
/// Kilit sırası: rezervasyon satırı, sonra ürünler (ProductId sırasıyla).
/// </summary>
public sealed class ReleaseStockConsumer(
    IReservationRepository reservations,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReleaseStockConsumer> logger) : IConsumer<ReleaseStock>
{
    public async Task Consume(ConsumeContext<ReleaseStock> context)
    {
        var orderId = context.Message.OrderId;
        var cancellationToken = context.CancellationToken;

        var reservation = await reservations.GetByOrderIdForUpdateAsync(orderId, cancellationToken);
        if (reservation is { Status: ReservationStatus.Reserved })
        {
            var lockedProducts = await products.GetForUpdateAsync(
                reservation.Items.Select(i => i.ProductId).Distinct().ToList(), cancellationToken);
            reservation.Release(lockedProducts, clock.GetUtcNow());
            logger.LogInformation("Stock released for order {OrderId}", orderId);
        }
        else
        {
            logger.LogInformation(
                "ReleaseStock for order {OrderId}: nothing to release ({Status})",
                orderId, reservation?.Status.ToString() ?? "no reservation");
        }

        await context.Publish(new StockReleased { OrderId = orderId, OccurredAt = clock.GetUtcNow() });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
