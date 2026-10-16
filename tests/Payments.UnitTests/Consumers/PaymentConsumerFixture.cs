using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Transactions;
using Payments.Application.Wallets;
using Payments.UnitTests.Fakes;
using Shared.Contracts.Payments.Commands;

namespace Payments.UnitTests.Consumers;

/// <summary>Consumer'ları in-memory transport + fake repository ile çalıştırır.</summary>
public abstract class PaymentConsumerFixture : IAsyncLifetime
{
    protected static readonly Guid RichCustomer = Guid.Parse("6f1c2b8e-3a4d-4f5e-9a7b-1c2d3e4f5a6b");
    protected static readonly Guid PoorCustomer = Guid.Parse("b2b2b2b2-0000-0000-0000-000000000002");

    private ServiceProvider _provider = null!;

    protected InMemoryPaymentStore Store { get; } = new();

    protected ITestHarness Harness { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Store.AddWallet(RichCustomer, "TRY", 1000m);
        Store.AddWallet(PoorCustomer, "TRY", 100m);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IPaymentRepository>(Store)
            .AddSingleton<IWalletRepository>(Store)
            .AddSingleton<IUnitOfWork>(Store);
        services.AddMassTransitTestHarness(x =>
        {
            x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(10));
            x.AddConsumer<ProcessPaymentConsumer>();
            x.AddConsumer<RefundPaymentConsumer>();
        });

        _provider = services.BuildServiceProvider(validateScopes: true);
        Harness = _provider.GetRequiredService<ITestHarness>();
        await Harness.Start();
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>Her çağrı yeni MessageId üretir: Inbox'ın yakalamadığı, saga'nın tekrar gönderdiği komutu taklit eder.</summary>
    protected Task ProcessAsync(Guid orderId, Guid customerId, decimal amount, string currency = "TRY") =>
        PublishAndWaitAsync(new ProcessPayment
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency,
            RequestedAt = DateTimeOffset.UtcNow,
        });

    protected Task RefundAsync(Guid orderId, decimal amount = 0m, string currency = "TRY") =>
        PublishAndWaitAsync(new RefundPayment
        {
            OrderId = orderId,
            Amount = amount,
            Currency = currency,
            Reason = "test",
            RequestedAt = DateTimeOffset.UtcNow,
        });

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
