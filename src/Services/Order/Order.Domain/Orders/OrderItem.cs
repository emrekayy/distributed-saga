namespace Order.Domain.Orders;

/// <summary>Sipariş kalemi (value object). Geçerlilik Order.Create'te kontrol edilir.</summary>
public sealed record OrderItem(Guid ProductId, int Quantity, decimal UnitPrice)
{
    public decimal LineTotal => Quantity * UnitPrice;
}
