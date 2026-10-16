using System.Collections.Concurrent;
using MassTransit;

namespace IntegrationTests.Support;

/// <summary>
/// Servisin bus'ına bağlanan gözlemci: mesaj tipi başına aynı anda çalışan consumer sayısının tepe değerini ve
/// teslimat sayısını kaydeder. Retry denemelerinin içini görmez (bunun için <see cref="DbFailureLog"/>).
/// </summary>
public sealed class ConsumeActivityObserver : IConsumeObserver
{
    private readonly ConcurrentDictionary<Type, int> _active = new();
    private readonly ConcurrentDictionary<Type, int> _peak = new();
    private readonly ConcurrentDictionary<Type, int> _deliveries = new();

    public int PeakConcurrency<T>() => _peak.GetValueOrDefault(typeof(T));

    /// <summary>Consumer'a ulaşan teslimat sayısı (Inbox'ın yakaladığı yinelenenler hariç).</summary>
    public int Deliveries<T>() => _deliveries.GetValueOrDefault(typeof(T));

    public Task PreConsume<T>(ConsumeContext<T> context)
        where T : class
    {
        _deliveries.AddOrUpdate(typeof(T), 1, (_, n) => n + 1);
        var now = _active.AddOrUpdate(typeof(T), 1, (_, n) => n + 1);
        _peak.AddOrUpdate(typeof(T), now, (_, peak) => Math.Max(peak, now));
        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context)
        where T : class
    {
        _active.AddOrUpdate(typeof(T), 0, (_, n) => n - 1);
        return Task.CompletedTask;
    }

    public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception)
        where T : class
    {
        _active.AddOrUpdate(typeof(T), 0, (_, n) => n - 1);
        return Task.CompletedTask;
    }
}
