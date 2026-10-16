using System.Collections.Concurrent;
using MassTransit;
using Shared.Contracts.Orders.Events;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;

namespace IntegrationTests.Support;

public sealed record ReceivedMessage(Guid OrderId, Guid? MessageId, object Message);

/// <summary>
/// Testin kendi bus'ı: gerçek RabbitMQ üzerinden komut gönderir (istenen MessageId ile) ve servislerin yayınladığı
/// sonuç event'lerini ile hata (Fault) mesajlarını OrderId'ye göre kaydeder.
/// </summary>
public sealed class MessageProbe : IAsyncDisposable
{
    private readonly IBusControl _bus;
    private readonly ConcurrentQueue<ReceivedMessage> _received = new();

    private MessageProbe(IBusControl bus, ConcurrentQueue<ReceivedMessage> received)
    {
        _bus = bus;
        _received = received;
    }

    public static async Task<MessageProbe> StartAsync(string host, ushort port, string vhost, string user, string password)
    {
        var received = new ConcurrentQueue<ReceivedMessage>();

        var bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(host, port, vhost, h =>
            {
                h.Username(user);
                h.Password(password);
            });

            cfg.ReceiveEndpoint($"integration-probe-{Guid.NewGuid():N}", e =>
            {
                e.Durable = false;
                e.AutoDelete = true;

                Track<OrderSubmitted>(e, received, m => m.OrderId);
                Track<OrderCompleted>(e, received, m => m.OrderId);
                Track<OrderFailed>(e, received, m => m.OrderId);
                Track<StockReserved>(e, received, m => m.OrderId);
                Track<StockReservationFailed>(e, received, m => m.OrderId);
                Track<StockReleased>(e, received, m => m.OrderId);
                Track<PaymentCompleted>(e, received, m => m.OrderId);
                Track<PaymentFailed>(e, received, m => m.OrderId);
                Track<PaymentRefunded>(e, received, m => m.OrderId);
                Track<Fault<ReserveStock>>(e, received, m => m.Message.OrderId);
                Track<Fault<ProcessPayment>>(e, received, m => m.Message.OrderId);
            });
        });

        await bus.StartAsync();
        return new MessageProbe(bus, received);
    }

    /// <summary>Komutu sabit kuyruğuna gönderir; <paramref name="messageId"/> verilirse MessageId olarak kullanılır.</summary>
    public async Task SendCommandAsync<T>(T command, string queueName, Guid? messageId = null)
        where T : class
    {
        var endpoint = await _bus.GetSendEndpoint(new Uri($"queue:{queueName}"));
        await endpoint.Send(command, context =>
        {
            if (messageId is { } id)
            {
                context.MessageId = id;
            }
        });
    }

    public IReadOnlyList<T> Of<T>(Guid orderId)
        where T : class =>
        _received.Where(r => r.OrderId == orderId && r.Message is T).Select(r => (T)r.Message).ToList();

    public IReadOnlyList<ReceivedMessage> ReceivedOf<T>(Guid orderId)
        where T : class =>
        _received.Where(r => r.OrderId == orderId && r.Message is T).ToList();

    public ValueTask DisposeAsync() => new(_bus.StopAsync());

    private static void Track<T>(IReceiveEndpointConfigurator endpoint, ConcurrentQueue<ReceivedMessage> sink, Func<T, Guid> orderId)
        where T : class =>
        endpoint.Handler<T>(context =>
        {
            sink.Enqueue(new ReceivedMessage(orderId(context.Message), context.MessageId, context.Message));
            return Task.CompletedTask;
        });
}
