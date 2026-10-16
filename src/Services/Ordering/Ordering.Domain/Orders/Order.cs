namespace Ordering.Domain.Orders;

/// <summary>
/// Sipariş aggregate'i. Pending olarak oluşur; saga sonucuna göre Complete() veya Fail() ile kapanır.
/// </summary>
public sealed class Order
{
    public const int MaxIdempotencyKeyLength = 100;

    private readonly List<OrderItem> _items = [];

    private Order()
    {
        // EF Core
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public decimal TotalAmount { get; private set; }

    /// <summary>ISO 4217 para birimi kodu.</summary>
    public string Currency { get; private set; } = null!;

    public OrderStatus Status { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>İstemcinin Idempotency-Key header'ı; unique.</summary>
    public string IdempotencyKey { get; private set; } = null!;

    /// <summary>İsteğin kanonik JSON'unun SHA-256'sı; aynı key ile farklı body'yi ayırt eder.</summary>
    public string RequestHash { get; private set; } = null!;

    public static Order Create(
        Guid id,
        Guid customerId,
        string currency,
        IReadOnlyCollection<OrderItem> items,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new OrderValidationException(nameof(Id), "Order id is required.");
        }

        if (customerId == Guid.Empty)
        {
            throw new OrderValidationException(nameof(CustomerId), "Customer id is required.");
        }

        if (currency is null || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new OrderValidationException(nameof(Currency), "Currency must be a 3-letter uppercase ISO 4217 code.");
        }

        ValidateItems(items);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw new OrderValidationException(
                nameof(IdempotencyKey), $"Idempotency key is required and must be at most {MaxIdempotencyKeyLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new OrderValidationException(nameof(RequestHash), "Request hash is required.");
        }

        var order = new Order
        {
            Id = id,
            CustomerId = customerId,
            Currency = currency,
            Status = OrderStatus.Pending,
            CreatedAt = createdAt,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
        };
        order._items.AddRange(items);
        order.TotalAmount = items.Sum(i => i.LineTotal);
        return order;
    }

    /// <summary>Pending → Completed. Zaten Completed ise no-op (tekrar teslimde güvenli).</summary>
    public void Complete()
    {
        if (Status == OrderStatus.Completed)
        {
            return;
        }

        EnsurePending(OrderStatus.Completed);
        Status = OrderStatus.Completed;
    }

    /// <summary>Pending → Failed. Zaten Failed ise no-op (ilk sebep korunur).</summary>
    public void Fail(string reasonCode, string reason)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new OrderValidationException(nameof(ReasonCode), "Reason code is required.");
        }

        if (Status == OrderStatus.Failed)
        {
            return;
        }

        EnsurePending(OrderStatus.Failed);
        Status = OrderStatus.Failed;
        ReasonCode = reasonCode;
        Reason = reason;
    }

    private void EnsurePending(OrderStatus target)
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOrderStateTransitionException(Id, Status, target);
        }
    }

    private static void ValidateItems(IReadOnlyCollection<OrderItem>? items)
    {
        if (items is null || items.Count == 0)
        {
            throw new OrderValidationException(nameof(Items), "Order must contain at least one item.");
        }

        foreach (var item in items)
        {
            if (item.ProductId == Guid.Empty)
            {
                throw new OrderValidationException(nameof(Items), "Product id is required.");
            }

            if (item.Quantity <= 0)
            {
                throw new OrderValidationException(nameof(Items), "Quantity must be greater than zero.");
            }

            if (item.UnitPrice <= 0)
            {
                throw new OrderValidationException(nameof(Items), "Unit price must be greater than zero.");
            }
        }

        if (items.Select(i => i.ProductId).Distinct().Count() != items.Count)
        {
            throw new OrderValidationException(nameof(Items), "Each product may appear only once.");
        }
    }
}
