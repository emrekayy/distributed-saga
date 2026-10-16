namespace Order.Domain.Orders;

/// <summary>Order aggregate'inin kural ihlallerinin ortak tabanı.</summary>
public abstract class OrderDomainException(string message) : Exception(message);

/// <summary>Sipariş oluşturulurken bir invariant ihlal edildi.</summary>
public sealed class OrderValidationException(string propertyName, string message) : OrderDomainException(message)
{
    public string PropertyName { get; } = propertyName;
}

/// <summary>İzin verilmeyen durum geçişi (örn. Completed → Failed).</summary>
public sealed class InvalidOrderStateTransitionException(Guid orderId, OrderStatus from, OrderStatus to)
    : OrderDomainException($"Order {orderId} cannot transition from {from} to {to}.")
{
    public OrderStatus From { get; } = from;

    public OrderStatus To { get; } = to;
}
