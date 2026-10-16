using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Web;

namespace BuildingBlocks.Messaging.Tests;

public sealed class HealthProbeTests
{
    [Theory]
    [InlineData(200, 0)]
    [InlineData(503, 1)]
    public async Task Probe_maps_http_status_to_exit_code(int status, int expectedExit)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        var serve = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = status;
            context.Response.Close();
        });

        var exit = await HealthProbe.ProbeAsync($"http://localhost:{port}/health/ready");
        await serve;

        Assert.Equal(expectedExit, exit);
    }

    [Fact]
    public async Task Probe_returns_one_when_nothing_listens()
    {
        var exit = await HealthProbe.ProbeAsync($"http://localhost:{FreePort()}/health/ready");

        Assert.Equal(1, exit);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
