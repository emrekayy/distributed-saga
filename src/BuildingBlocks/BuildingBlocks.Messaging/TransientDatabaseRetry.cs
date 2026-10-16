using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Saga ve consumer endpoint'lerinin ortak retry kapsamı. Yalnızca tekrar denendiğinde başarılı
/// olabilecek veritabanı hataları retry'a girer:
/// <list type="bullet">
/// <item>Optimistic concurrency çakışması (xmin): yeniden yüklenen güncel kayıtla tekrar işlenir.</item>
/// <item>Unique violation (23505): örn. aynı CorrelationId ile eşzamanlı ilk insert; retry'da kayıt bulunur.</item>
/// <item>Geçici Npgsql hataları (bağlantı kopması, timeout): <see cref="NpgsqlException.IsTransient"/>.</item>
/// </list>
/// Domain/validation hataları retry'a girmez; çağıran taraf bunları ayrıca Ignore eder.
/// </summary>
public static class TransientDatabaseRetry
{
    public static bool IsRetryable(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => true,
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => true,
        DbUpdateException { InnerException: NpgsqlException { IsTransient: true } } => true,
        NpgsqlException { IsTransient: true } => true,
        _ => false,
    };

    /// <summary>
    /// Üstel bekleme ile retry. MassTransit'in Exponential politikası her aralığı delta'nın ±%20'si içinde
    /// rastgele seçer (jitter); eşzamanlı çakışan mesajlar aynı anda tekrar denenmez.
    /// </summary>
    public static void UseTransientDatabaseRetry(this IRetryConfigurator retry, params Type[] nonRetryableExceptions)
    {
        retry.Exponential(5, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100));
        retry.Handle<Exception>(IsRetryable);
        if (nonRetryableExceptions.Length > 0)
        {
            retry.Ignore(nonRetryableExceptions);
        }
    }
}
