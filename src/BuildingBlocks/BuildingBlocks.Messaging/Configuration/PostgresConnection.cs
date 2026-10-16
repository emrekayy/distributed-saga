using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BuildingBlocks.Messaging.Configuration;

public static class PostgresConnection
{
    /// <summary>
    /// .env anahtarlarından servis connection string'i kurar: POSTGRES_HOST, POSTGRES_PORT,
    /// {PREFIX}_DB_NAME, {PREFIX}_DB_USER, {PREFIX}_DB_PASSWORD (örn. prefix "ORDER").
    /// </summary>
    public static string Build(IConfiguration configuration, string servicePrefix) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = configuration["POSTGRES_HOST"] ?? "localhost",
            Port = configuration.GetValue("POSTGRES_PORT", 5432),
            Database = configuration.GetRequired($"{servicePrefix}_DB_NAME"),
            Username = configuration.GetRequired($"{servicePrefix}_DB_USER"),
            Password = configuration.GetRequired($"{servicePrefix}_DB_PASSWORD"),
        }.ConnectionString;
}
