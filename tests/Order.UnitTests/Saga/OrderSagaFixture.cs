using BuildingBlocks.Messaging;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Order.Application.Orders;
using Order.Application.Saga;
using Order.UnitTests.Fakes;
using Shared.Contracts.Common;
using Shared.Contracts.Orders.Events;

namespace Order.UnitTests.Saga;

using Order.Domain.Orders;

/// <summary>
/// Saga'yı in-memory transport + in-memory saga repository ile çalıştırır. Order aggregate'i fake
/// repository'de tutulur; activity'ler onu günceller.
/// </summary>
public abstract class OrderSagaFixture : IAsyncLifetime
{
    protected static readonly Guid ProductA = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    protected static readonly Guid ProductB = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002");

    private ServiceProvider _provider = null!;

    protected InMemoryOrderStore Orders { get; } = new();

    protected ITestHarness Harness { get; private set; } = null!;

    protected ISagaStateMachineTestHarness<OrderStateMachine, OrderState> Saga { get; private set; } = null!;

    protected OrderStateMachine Machine { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        CommandEndpoints.Map();

        var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IOrderRepository>(Orders);
        ConfigureServices(services);
        services.AddMassTransitTestHarness(x =>
        {
            x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(10));
            x.AddSagaStateMachine<OrderStateMachine, OrderState>().InMemoryRepository();
            ConfigureBus(x);
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        Harness = _provider.GetRequiredService<ITestHarness>();
        await Harness.Start();
        Saga = Harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();
        Machine = Saga.StateMachine;
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected virtual void ConfigureBus(IBusRegistrationConfigurator configurator)
    {
    }

    /// <summary>Fake repository'ye Pending sipariş koyar ve OrderSubmitted publish eder → AwaitingStock.</summary>
    protected async Task<Guid> SubmitOrderAsync(decimal? totalAmountOverride = null)
    {
        var orderId = Guid.NewGuid();
        var order = Order.Create(
            orderId,
            Guid.NewGuid(),
            "TRY",
            [new OrderItem(ProductA, 2, 150m), new OrderItem(ProductB, 1, 99.90m)],
            $"key-{orderId}",
            "hash",
            DateTimeOffset.UtcNow);
        Orders.Seed(order);

        await Harness.Bus.Publish(new OrderSubmitted
        {
            OrderId = orderId,
            CustomerId = order.CustomerId,
            Items = order.Items
                .Select(i => new OrderItemDto { ProductId = i.ProductId, Quantity = i.Quantity, UnitPrice = i.UnitPrice })
                .ToList(),
            TotalAmount = totalAmountOverride ?? order.TotalAmount,
            Currency = order.Currency,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await AssertStateAsync(orderId, Machine.AwaitingStock);
        return orderId;
    }

    /// <summary>Mesajı publish eder ve saga'nın o MessageId'yi tükettiğini bekler (Ignore edilse de).</summary>
    protected async Task PublishAndWaitAsync<T>(T message)
        where T : class
    {
        var messageId = NewId.NextGuid();
        await Harness.Bus.Publish(message, ctx => ctx.MessageId = messageId);
        await WaitUntilAsync(
            () => Saga.Consumed.Select<T>(x => x.Context.MessageId == messageId).Any(),
            $"{typeof(T).Name} was not consumed by the saga.");
    }

    /// <summary>
    /// Harness'ın Any() beklemeleri inactivity timeout'a bağlıdır; Exists() gibi yoklamalar aktivite sayılmaz
    /// ve Any() mesaj işlenmeden false dönebilir. Kayıtlı mesajları senkron yoklamak deterministiktir.
    /// </summary>
    protected static async Task WaitUntilAsync(Func<bool> condition, string failureMessage)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failureMessage);
            await Task.Delay(10);
        }
    }

    protected async Task AssertStateAsync(Guid orderId, State expected)
    {
        var found = await Saga.Exists(orderId, _ => expected);
        Assert.True(found.HasValue, $"Saga {orderId} did not reach state {expected.Name}.");
    }

    protected Order GetOrder(Guid orderId) =>
        Orders.Committed.Single(o => o.Id == orderId);

    /// <summary>Saga'nın tükettiği hiçbir mesaj exception üretmedi (hata kuyruğuna düşen yok).</summary>
    protected void AssertNoSagaFaults() =>
        Assert.DoesNotContain(Saga.Consumed.Select(_ => true), m => m.Exception is not null);

    protected int SentCount<T>(Guid orderId)
        where T : class =>
        Harness.Sent.Select<T>().Count(x => OrderIdOf(x.Context.Message) == orderId);

    protected int PublishedCount<T>(Guid orderId)
        where T : class =>
        Harness.Published.Select<T>().Count(x => OrderIdOf(x.Context.Message) == orderId);

    protected static Guid OrderIdOf(object message) =>
        (Guid)message.GetType().GetProperty("OrderId")!.GetValue(message)!;
}
