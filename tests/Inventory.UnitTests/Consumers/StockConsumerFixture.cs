using Inventory.Application.Products;
using Inventory.Application.Reservations;
using Inventory.UnitTests.Fakes;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Common;
using Shared.Contracts.Stock.Commands;

namespace Inventory.UnitTests.Consumers;

/// <summary>Consumer'ları in-memory transport + fake repository ile çalıştırır.</summary>
public abstract class StockConsumerFixture : IAsyncLifetime
{
    protected static readonly Guid Keyboard = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    protected static readonly Guid Monitor = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000003");

    private ServiceProvider _provider = null!;

    protected InMemoryInventoryStore Store { get; } = new();

    protected ITestHarness Harness { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Store.AddProduct(Keyboard, "Keyboard", 10);
        Store.AddProduct(Monitor, "Monitor", 0);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IProductRepository>(Store)
            .AddSingleton<IReservationRepository>(Store)
            .AddSingleton<IUnitOfWork>(Store);
        services.AddMassTransitTestHarness(x =>
        {
            x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(10));
            x.AddConsumer<ReserveStockConsumer>();
            x.AddConsumer<ReleaseStockConsumer>();
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        Harness = _provider.GetRequiredService<ITestHarness>();
        await Harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>Her çağrı yeni MessageId üretir: Inbox'ın yakalamadığı, saga'nın tekrar gönderdiği komutu taklit eder.</summary>
    protected Task ReserveAsync(Guid orderId, params (Guid ProductId, int Quantity)[] items) =>
        PublishAndWaitAsync<ReserveStock>(new ReserveStock
        {
            OrderId = orderId,
            Items = items.Select(i => new OrderItemDto { ProductId = i.ProductId, Quantity = i.Quantity, UnitPrice = 10m }).ToList(),
            RequestedAt = DateTimeOffset.UtcNow,
        });

    protected Task ReleaseAsync(Guid orderId) =>
        PublishAndWaitAsync<ReleaseStock>(new ReleaseStock { OrderId = orderId, RequestedAt = DateTimeOffset.UtcNow });

    protected int PublishedCount<T>(Guid orderId)
        where T : class =>
        Harness.Published.Select<T>().Count(x => OrderIdOf(x.Context.Message) == orderId);

    protected T LastPublished<T>(Guid orderId)
        where T : class =>
        Harness.Published.Select<T>().Where(x => OrderIdOf(x.Context.Message) == orderId).Last().Context.Message;

    private async Task PublishAndWaitAsync<T>(T message)
        where T : class
    {
        var messageId = NewId.NextGuid();
        await Harness.Bus.Publish(message, ctx => ctx.MessageId = messageId);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!Harness.Consumed.Select<T>(x => x.Context.MessageId == messageId).Any())
        {
            Assert.True(DateTime.UtcNow < deadline, $"{typeof(T).Name} was not consumed.");
            await Task.Delay(10);
        }

        Assert.DoesNotContain(Harness.Consumed.Select<T>(x => x.Context.MessageId == messageId), m => m.Exception is not null);
    }

    private static Guid OrderIdOf(object message) =>
        (Guid)message.GetType().GetProperty("OrderId")!.GetValue(message)!;
}
