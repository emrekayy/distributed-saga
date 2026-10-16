using MassTransit.Testing;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Orders;
using Ordering.Domain.Orders;
using Ordering.UnitTests.Fakes;
using Shared.Contracts.Orders.Events;

namespace Ordering.UnitTests.Application;

public sealed class SubmitOrderHandlerTests : IAsyncLifetime
{
    private readonly InMemoryOrderStore _store = new();
    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .BuildServiceProvider(validateScopes: true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task New_key_creates_pending_order_and_publishes_OrderSubmitted()
    {
        var result = await HandleAsync(Command("key-1"));

        Assert.True(result.Created);
        Assert.Equal(OrderStatus.Pending, result.Order.Status);
        Assert.Single(_store.Committed);

        var published = Assert.Single(_harness.Published.Select<OrderSubmitted>()).Context.Message;
        Assert.Equal(result.Order.Id, published.OrderId);
        Assert.Equal(result.Order.TotalAmount, published.TotalAmount);
        Assert.Equal("TRY", published.Currency);
        Assert.Equal(2, published.Items.Count);
    }

    [Fact]
    public async Task Same_key_and_body_returns_existing_order_without_publishing_again()
    {
        var first = await HandleAsync(Command("key-1"));

        var second = await HandleAsync(Command("key-1", reverseItems: true));

        Assert.False(second.Created);
        Assert.Equal(first.Order.Id, second.Order.Id);
        Assert.Single(_store.Committed);
        Assert.Single(_harness.Published.Select<OrderSubmitted>());
    }

    [Fact]
    public async Task Same_key_with_different_body_is_a_conflict()
    {
        await HandleAsync(Command("key-1"));

        await Assert.ThrowsAsync<IdempotencyKeyConflictException>(() => HandleAsync(Command("key-1", quantity: 5)));
        Assert.Single(_store.Committed);
    }

    [Fact]
    public async Task Concurrent_request_with_same_key_returns_the_winner()
    {
        var winner = Order.Create(
            Guid.NewGuid(), Customer, "TRY", [new OrderItem(ProductA, 2, 150m), new OrderItem(ProductB, 1, 99.90m)],
            "key-1", RequestHasher.Compute(Command("key-1")), DateTimeOffset.UtcNow);
        _store.ConcurrentWinner = winner;

        var result = await HandleAsync(Command("key-1"));

        Assert.False(result.Created);
        Assert.Equal(winner.Id, result.Order.Id);
    }

    [Fact]
    public async Task Invalid_order_is_rejected_before_saving()
    {
        await Assert.ThrowsAsync<OrderValidationException>(() => HandleAsync(Command("key-1", quantity: 0)));
        Assert.Equal(0, _store.SaveCount);
    }

    private static readonly Guid Customer = Guid.Parse("6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b");
    private static readonly Guid ProductA = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid ProductB = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002");

    private static SubmitOrderCommand Command(string key, int quantity = 2, bool reverseItems = false)
    {
        SubmitOrderItem[] items = [new(ProductA, quantity, 150m), new(ProductB, 1, 99.90m)];
        return new SubmitOrderCommand(Customer, "TRY", reverseItems ? Enumerable.Reverse(items).ToArray() : items, key);
    }

    private async Task<SubmitOrderResult> HandleAsync(SubmitOrderCommand command)
    {
        await using var scope = _provider.CreateAsyncScope();
        var handler = new SubmitOrderHandler(
            _store, _store, scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), TimeProvider.System);
        return await handler.HandleAsync(command, CancellationToken.None);
    }
}
