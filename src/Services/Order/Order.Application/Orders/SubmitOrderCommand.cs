namespace Order.Application.Orders;

public sealed record SubmitOrderCommand(
    Guid CustomerId,
    string Currency,
    IReadOnlyList<SubmitOrderItem> Items,
    string IdempotencyKey);

/// <summary>Fiyat istemciden gelir: bilinçli sadeleştirme, gerçek sistemde fiyat katalogdan okunur.</summary>
public sealed record SubmitOrderItem(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record SubmitOrderResult(Domain.Orders.Order Order, bool Created);
