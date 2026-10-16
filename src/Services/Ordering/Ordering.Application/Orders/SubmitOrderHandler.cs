using MassTransit;
using Ordering.Domain.Orders;
using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;

namespace Ordering.Application.Orders;

/// <summary>
/// POST /orders use case'i. Siparişi Pending kaydeder ve OrderSubmitted'ı bus outbox üzerinden
/// aynı SaveChanges'te yazar. Idempotency-Key tekrarında mevcut sipariş döner; body farklıysa 422.
/// </summary>
public sealed class SubmitOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IPublishEndpoint publishEndpoint,
    TimeProvider timeProvider)
{
    public async Task<SubmitOrderResult> HandleAsync(SubmitOrderCommand command, CancellationToken cancellationToken)
    {
        var requestHash = RequestHasher.Compute(command);

        var existing = await orders.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return Replay(existing, requestHash);
        }

        var order = Order.Create(
            Guid.CreateVersion7(),
            command.CustomerId,
            command.Currency,
            (command.Items ?? []).Select(i => new OrderItem(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
            command.IdempotencyKey,
            requestHash,
            timeProvider.GetUtcNow());

        orders.Add(order);

        // Bus outbox: mesaj broker'a değil, aynı DbContext'teki OutboxMessage tablosuna yazılır.
        await publishEndpoint.Publish(
            new OrderSubmitted
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Items = order.Items
                    .Select(i => new OrderItemDto { ProductId = i.ProductId, Quantity = i.Quantity, UnitPrice = i.UnitPrice })
                    .ToList(),
                TotalAmount = order.TotalAmount,
                Currency = order.Currency,
                OccurredAt = order.CreatedAt,
            },
            cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // Aynı key ile eşzamanlı istek kazandı: onun siparişi bu isteğin de cevabıdır.
            var winner = await orders.GetByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Order with the duplicate idempotency key was not found.");
            return Replay(winner, requestHash);
        }

        return new SubmitOrderResult(order, Created: true);
    }

    private static SubmitOrderResult Replay(Order existing, string requestHash) =>
        existing.RequestHash == requestHash
            ? new SubmitOrderResult(existing, Created: false)
            : throw new IdempotencyKeyConflictException(existing.IdempotencyKey);
}
