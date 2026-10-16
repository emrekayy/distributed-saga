using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

namespace BuildingBlocks.Messaging.Configuration;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Yalnızca yerel geliştirme: repo kökündeki .env dosyasını (KEY=VALUE) konfigürasyona ekler.
    /// Ortam değişkenleri yeniden eklenir, böylece .env'yi her zaman ezer. Container'da .env yoktur,
    /// aynı anahtarlar compose tarafından ortam değişkeni olarak verilir.
    /// </summary>
    public static IConfigurationBuilder AddDotEnvFromParentDirectories(this IConfigurationBuilder builder, string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".env")))
            {
                // PhysicalFileProvider varsayılan olarak "." ile başlayan dosyaları gizler; .env için kapatılmalı.
                var fileProvider = new PhysicalFileProvider(dir.FullName, ExclusionFilters.None);
                builder.AddIniFile(fileProvider, ".env", optional: false, reloadOnChange: false);
                builder.AddEnvironmentVariables();
                break;
            }
        }

        return builder;
    }

    /// <summary>Zorunlu anahtarı okur; yoksa değeri değil yalnızca anahtar adını içeren bir hata fırlatır.</summary>
    public static string GetRequired(this IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Configuration value '{key}' is missing (see .env.example).");
}
