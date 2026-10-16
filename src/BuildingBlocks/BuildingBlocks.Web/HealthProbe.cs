namespace BuildingBlocks.Web;

/// <summary>
/// Shell'siz (chiseled) container'da compose healthcheck'i için: <c>dotnet X.API.dll --health-probe http://localhost:8080/health/ready</c>.
/// Verilen adrese GET atar, 2xx ise 0, aksi halde 1 koduyla process'ten çıkar. Argüman yoksa hiçbir şey yapmaz.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--health-probe";

    public static async Task RunIfRequestedAsync(string[] args)
    {
        if (args is not [Argument, var url])
        {
            return;
        }

        Environment.Exit(await ProbeAsync(url));
    }

    public static async Task<int> ProbeAsync(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
