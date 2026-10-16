using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.Options;
using Npgsql;
using Shared.Contracts.Payments.Commands;

namespace Payments.Infrastructure.FaultInjection;

/// <summary>Retry'a girmeyen enjekte hata (kalıcı arıza simülasyonu); mesaj _error kuyruğuna düşer.</summary>
public sealed class InjectedPermanentFailureException() : Exception("Injected permanent failure (FaultInjection:PermanentFailure).");

/// <summary>
/// ProcessPayment consumer'ının önünde, retry ve EF outbox'ın içinde çalışır: enjekte edilen geçici hata gerçek bir
/// geçici veritabanı hatası gibi (<see cref="NpgsqlException"/>, timeout) retry politikasına takılır; consumer
/// çalışmadan önce fırlatıldığı için yan etki bırakmaz. Production'da kaydedilmez.
/// </summary>
public sealed class FaultInjectionFilter(IOptionsMonitor<FaultInjectionOptions> options) : IFilter<ConsumeContext<ProcessPayment>>
{
    private readonly ConcurrentDictionary<Guid, int> _failuresPerMessage = new();
    private int _injectedTransientFailures;

    public int InjectedTransientFailures => Volatile.Read(ref _injectedTransientFailures);

    public void Probe(ProbeContext context) => context.CreateFilterScope("faultInjection");

    public async Task Send(ConsumeContext<ProcessPayment> context, IPipe<ConsumeContext<ProcessPayment>> next)
    {
        var settings = options.CurrentValue;
        if (settings.Enabled)
        {
            if (settings.Delay > TimeSpan.Zero)
            {
                await Task.Delay(settings.Delay, context.CancellationToken);
            }

            if (settings.PermanentFailure)
            {
                throw new InjectedPermanentFailureException();
            }

            var key = context.MessageId ?? context.Message.OrderId;
            if (settings.TransientFailureRate > 0
                && _failuresPerMessage.GetValueOrDefault(key) < settings.MaxTransientFailuresPerMessage
                && Random.Shared.NextDouble() < settings.TransientFailureRate)
            {
                _failuresPerMessage.AddOrUpdate(key, 1, (_, n) => n + 1);
                Interlocked.Increment(ref _injectedTransientFailures);
                throw new NpgsqlException("Injected transient failure.", new TimeoutException());
            }
        }

        _failuresPerMessage.TryRemove(key: context.MessageId ?? context.Message.OrderId, out _);
        await next.Send(context);
    }
}
