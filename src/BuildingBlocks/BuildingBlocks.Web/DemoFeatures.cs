using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web;

/// <summary>
/// Demo amaçlı bayraklar; varsayılan kapalı. Development ve compose demosunda açılır:
/// <c>Seed:Enabled</c> (demo verisi) ve <c>InternalEndpoints:Enabled</c> (/internal/* tanı endpoint'leri).
/// </summary>
public static class DemoFeatures
{
    public const string SeedKey = "Seed:Enabled";
    public const string InternalEndpointsKey = "InternalEndpoints:Enabled";

    public static bool IsSeedEnabled(this IConfiguration configuration) => configuration.GetValue<bool>(SeedKey);

    /// <summary>Production'da açıksa startup'ta uyarı loglar: bu endpoint'ler kimlik doğrulamasızdır, yalnızca demo içindir.</summary>
    public static bool AreInternalEndpointsEnabled(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>(InternalEndpointsKey))
        {
            return false;
        }

        if (app.Environment.IsProduction())
        {
            app.Logger.LogWarning(
                "{Key}=true in Production: unauthenticated /internal/* diagnostic endpoints are exposed. Demo only.",
                InternalEndpointsKey);
        }

        return true;
    }
}
