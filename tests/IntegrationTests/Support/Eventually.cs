namespace IntegrationTests.Support;

/// <summary>
/// Testlerdeki tek bekleme mekanizması: koşul sağlanana kadar timeout'lu yoklama. Sabit süreli bekleme (sleep) yok;
/// süre dolarsa neyin beklendiğini ve son hatayı söyleyen açıklayıcı bir hata verir.
/// </summary>
public static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(50);

    public static Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null) =>
        UntilAsync(() => Task.FromResult(condition()), because, timeout);

    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var limit = timeout ?? DefaultTimeout;
        var started = TimeProvider.System.GetTimestamp();
        Exception? lastError = null;

        while (true)
        {
            try
            {
                if (await condition())
                {
                    return;
                }

                lastError = null;
            }
            catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
            {
                lastError = exception;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= limit)
            {
                Assert.Fail($"Timed out after {limit.TotalSeconds:0.#}s waiting for: {because}"
                    + (lastError is null ? string.Empty : $" (last error: {lastError.GetType().Name}: {lastError.Message})"));
            }

            await Task.Delay(_pollInterval);
        }
    }

    /// <summary>Probe null dönmeyene kadar yoklar ve değeri verir.</summary>
    public static async Task<T> ValueAsync<T>(Func<Task<T?>> probe, string because, TimeSpan? timeout = null)
        where T : class
    {
        T? found = null;
        await UntilAsync(async () => (found = await probe()) is not null, because, timeout);
        return found!;
    }
}
