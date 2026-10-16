using System.Collections.Concurrent;
using MassTransit;
using Npgsql;

namespace IntegrationTests.Support;

/// <summary>
/// Servisin bus'ına bağlanan gözlemci: mesaj tipi başına aynı anda çalışan consumer sayısının tepe değerini ve
/// consumer'lardan sızan veritabanı hatalarını (SqlState) kaydeder. Retry consumer'ın dışında olduğundan,
/// her başarısız deneme (örn. 40P01 deadlock, 23505 unique) burada görünür.
/// </summary>
public sealed class ConsumeActivityObserver : IConsumeObserver
{
    private readonly ConcurrentDictionary<Type, int> _active = new();
    private readonly ConcurrentDictionary<Type, int> _peak = new();
    private readonly ConcurrentDictionary<Type, int> _attempts = new();
    private readonly ConcurrentQueue<string> _sqlStates = new();

    public int PeakConcurrency<T>() => _peak.GetValueOrDefault(typeof(T));

    /// <summary>Consumer'ın çağrılma sayısı (retry denemeleri dahil).</summary>
    public int Attempts<T>() => _attempts.GetValueOrDefault(typeof(T));

    public int FaultsWithSqlState(string sqlState) => _sqlStates.Count(s => s == sqlState);

    public Task PreConsume<T>(ConsumeContext<T> context)
        where T : class
    {
        _attempts.AddOrUpdate(typeof(T), 1, (_, n) => n + 1);
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
        for (var inner = exception; inner is not null; inner = inner.InnerException!)
        {
            if (inner is PostgresException { SqlState: { } sqlState })
            {
                _sqlStates.Enqueue(sqlState);
                break;
            }

            if (inner.InnerException is null)
            {
                break;
            }
        }

        return Task.CompletedTask;
    }
}
