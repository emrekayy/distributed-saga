using Ordering.Domain.Orders;

namespace Ordering.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_computes_total_and_starts_pending()
    {
        var order = Create(items: [Item(quantity: 2, unitPrice: 150.00m), Item(quantity: 1, unitPrice: 99.90m)]);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(399.90m, order.TotalAmount);
        Assert.Equal(2, order.Items.Count);
        Assert.Null(order.ReasonCode);
    }

    public static TheoryData<string, Func<Order>> InvalidOrders => new()
    {
        { "Items", () => Create(items: []) },
        { "Items", () => Create(items: [Item(quantity: 0)]) },
        { "Items", () => Create(items: [Item(quantity: -1)]) },
        { "Items", () => Create(items: [Item(unitPrice: 0m)]) },
        { "Items", () => Create(items: [Item(unitPrice: -5m)]) },
        { "Items", () => Create(items: [new OrderItem(Guid.Empty, 1, 10m)]) },
        { "Items", () => Create(items: DuplicateProductItems()) },
        { "Currency", () => Create(currency: "TL") },
        { "Currency", () => Create(currency: "try") },
        { "Currency", () => Create(currency: "TRYX") },
        { "Currency", () => Create(currency: "T1Y") },
        { "CustomerId", () => Create(customerId: Guid.Empty) },
        { "IdempotencyKey", () => Create(idempotencyKey: " ") },
        { "IdempotencyKey", () => Create(idempotencyKey: new string('k', Order.MaxIdempotencyKeyLength + 1)) },
    };

    [Theory]
    [MemberData(nameof(InvalidOrders))]
    public void Create_rejects_invalid_input(string property, Func<Order> create)
    {
        var ex = Assert.Throws<OrderValidationException>(() => create());
        Assert.Equal(property, ex.PropertyName);
    }

    [Fact]
    public void Complete_moves_pending_to_completed_and_is_idempotent()
    {
        var order = Create();

        order.Complete();
        order.Complete();

        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    [Fact]
    public void Fail_records_reason_and_keeps_first_reason_on_repeat()
    {
        var order = Create();

        order.Fail("INSUFFICIENT_STOCK", "Not enough stock");
        order.Fail("PAYMENT_DECLINED", "ignored");

        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.Equal("INSUFFICIENT_STOCK", order.ReasonCode);
        Assert.Equal("Not enough stock", order.Reason);
    }

    [Fact]
    public void Completed_order_cannot_fail()
    {
        var order = Create();
        order.Complete();

        var ex = Assert.Throws<InvalidOrderStateTransitionException>(() => order.Fail("PAYMENT_TIMEOUT", "late"));
        Assert.Equal(OrderStatus.Completed, ex.From);
        Assert.Equal(OrderStatus.Failed, ex.To);
    }

    [Fact]
    public void Failed_order_cannot_complete()
    {
        var order = Create();
        order.Fail("PAYMENT_DECLINED", "declined");

        Assert.Throws<InvalidOrderStateTransitionException>(order.Complete);
        Assert.Equal(OrderStatus.Failed, order.Status);
    }

    [Fact]
    public void Fail_requires_reason_code()
    {
        var order = Create();

        Assert.Throws<OrderValidationException>(() => order.Fail(" ", "no code"));
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    private static OrderItem Item(int quantity = 1, decimal unitPrice = 10m) => new(Guid.NewGuid(), quantity, unitPrice);

    private static OrderItem[] DuplicateProductItems()
    {
        var productId = Guid.NewGuid();
        return [new OrderItem(productId, 1, 10m), new OrderItem(productId, 2, 10m)];
    }

    private static Order Create(
        Guid? customerId = null,
        string currency = "TRY",
        OrderItem[]? items = null,
        string idempotencyKey = "key-1") =>
        Order.Create(
            Guid.NewGuid(),
            customerId ?? Guid.NewGuid(),
            currency,
            items ?? [Item()],
            idempotencyKey,
            requestHash: "hash",
            _now);
}
