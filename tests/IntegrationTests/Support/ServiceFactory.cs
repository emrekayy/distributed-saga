using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IntegrationTests.Support;

/// <summary>
/// Bir servis API'sini test process'inde ayağa kaldırır. Ortam <c>Testing</c>: Development'a özgü .env yükleme,
/// startup migration ve seed çalışmaz; ayarlar bellek içi konfigürasyondan (container adresleri) gelir.
/// </summary>
internal sealed class ServiceFactory<TMarker>(
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TMarker>
    where TMarker : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        if (configureServices is not null)
        {
            builder.ConfigureServices(configureServices);
        }
    }
}
