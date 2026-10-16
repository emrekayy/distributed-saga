namespace IntegrationTests.Support;

/// <summary>
/// Yük sırasında bir değeri (stok, bakiye) arka planda sürekli okuyup görülen en küçük değeri tutar.
/// Yardımcı kanıt: asıl garanti veritabanındaki CHECK kısıtıdır; bu yoklama kısıtın dışında bir an olmadığını gösterir.
/// </summary>
public sealed class ValueWatcher : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private long _samples;
    private decimal _min = decimal.MaxValue;

    private ValueWatcher(Func<CancellationToken, Task<decimal>> read) => _loop = Task.Run(() => RunAsync(read));

    public decimal Min => _min;

    public long Samples => Interlocked.Read(ref _samples);

    public static ValueWatcher Start(Func<CancellationToken, Task<decimal>> read) => new(read);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
    }

    private async Task RunAsync(Func<CancellationToken, Task<decimal>> read)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var value = await read(_stop.Token);
                _min = Math.Min(_min, value);
                Interlocked.Increment(ref _samples);
                await Task.Delay(5, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
