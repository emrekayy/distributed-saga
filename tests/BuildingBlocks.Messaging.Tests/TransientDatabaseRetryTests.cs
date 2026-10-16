using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BuildingBlocks.Messaging.Tests;

public class TransientDatabaseRetryTests
{
    public static TheoryData<string, Exception, bool> Cases => new()
    {
        { "optimistic concurrency (xmin)", new DbUpdateConcurrencyException("conflict"), true },
        { "unique violation 23505", DbUpdate(Postgres(PostgresErrorCodes.UniqueViolation)), true },
        { "foreign key violation 23503", DbUpdate(Postgres(PostgresErrorCodes.ForeignKeyViolation)), false },
        { "transient npgsql (timeout)", new NpgsqlException("timeout", new TimeoutException()), true },
        { "transient npgsql wrapped in DbUpdateException", DbUpdate(new NpgsqlException("io", new IOException())), true },
        { "non-transient npgsql", new NpgsqlException("bad"), false },
        { "domain/validation error", new InvalidOperationException("invalid transition"), false },
        { "argument error", new ArgumentException("bad input"), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Classifies_database_exceptions(string scenario, Exception exception, bool expected)
    {
        Assert.True(expected == TransientDatabaseRetry.IsRetryable(exception), scenario);
    }

    private static PostgresException Postgres(string sqlState) => new("message", "ERROR", "ERROR", sqlState);

    private static DbUpdateException DbUpdate(Exception inner) => new("update failed", inner);
}
