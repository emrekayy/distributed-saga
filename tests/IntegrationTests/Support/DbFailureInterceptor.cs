using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace IntegrationTests.Support;

/// <summary>
/// Bir servisin EF üzerinden gördüğü her Postgres hatasını (SqlState ile) kaydeder. Retry consumer'ın dışında ve
/// mesaj gözlemcisinin içinde kaldığı için, retry ile gizlenen hatalar (40P01 deadlock, 23505 unique ihlali)
/// yalnızca burada görünür. Komut hataları ve SaveChanges hataları aynı exception için bir kez sayılır.
/// </summary>
public sealed class DbFailureLog
{
    private readonly ConcurrentQueue<string> _sqlStates = new();
    private readonly ConcurrentDictionary<PostgresException, byte> _seen = new(ReferenceEqualityComparer.Instance);

    public DbFailureLog()
    {
        Commands = new CommandInterceptor(this);
        SaveChanges = new SaveInterceptor(this);
    }

    public IInterceptor Commands { get; }

    public IInterceptor SaveChanges { get; }

    public string Summary => string.Join(",", _sqlStates.GroupBy(x => x).Select(g => $"{g.Key}x{g.Count()}"));

    public int Count(string sqlState) => _sqlStates.Count(s => s == sqlState);

    public void Attach(DbContextOptionsBuilder options) => options.AddInterceptors(Commands, SaveChanges);

    private void Record(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: { } sqlState } postgres)
            {
                if (_seen.TryAdd(postgres, 0))
                {
                    _sqlStates.Enqueue(sqlState);
                }

                return;
            }
        }
    }

    private sealed class CommandInterceptor(DbFailureLog log) : DbCommandInterceptor
    {
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            log.Record(eventData.Exception);
            return Task.CompletedTask;
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) => log.Record(eventData.Exception);
    }

    private sealed class SaveInterceptor(DbFailureLog log) : SaveChangesInterceptor
    {
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            log.Record(eventData.Exception);
            return Task.CompletedTask;
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData) => log.Record(eventData.Exception);
    }
}
