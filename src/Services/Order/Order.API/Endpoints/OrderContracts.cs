namespace Order.API.Endpoints;

using Order.Domain.Orders;

public sealed record SubmitOrderRequest(Guid CustomerId, string? Currency, IReadOnlyList<SubmitOrderItemRequest>? Items);

public sealed record SubmitOrderItemRequest(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderItemResponse(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record OrderResponse(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal TotalAmount,
    string Currency,
    IReadOnlyList<OrderItemResponse> Items,
    string? ReasonCode,
    string? Reason,
    DateTimeOffset CreatedAt)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Status.ToString(),
        order.TotalAmount,
        order.Currency,
        order.Items.Select(i => new OrderItemResponse(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
        order.ReasonCode,
        order.Reason,
        order.CreatedAt);
}
