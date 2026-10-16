using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Tests;

public sealed class DemoFeaturesTests
{
    [Fact]
    public void Seed_is_disabled_by_default()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.False(configuration.IsSeedEnabled());
    }

    [Fact]
    public void Seed_is_enabled_only_when_flag_is_true()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DemoFeatures.SeedKey] = "true" })
            .Build();

        Assert.True(configuration.IsSeedEnabled());
    }

    [Theory]
    [InlineData("Production", null, false, false)]
    [InlineData("Production", "false", false, false)]
    [InlineData("Production", "true", true, true)]
    [InlineData("Development", "true", true, false)]
    public void Internal_endpoints_follow_flag_and_warn_only_in_production(
        string environment, string? flag, bool expectedEnabled, bool expectedWarning)
    {
        var provider = new CapturingLoggerProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Logging.ClearProviders().AddProvider(provider);
        if (flag is not null)
        {
            builder.Configuration[DemoFeatures.InternalEndpointsKey] = flag;
        }

        var app = builder.Build();

        Assert.Equal(expectedEnabled, app.AreInternalEndpointsEnabled());
        Assert.Equal(expectedWarning, provider.Warnings.Count > 0);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capture(Warnings);

        public void Dispose()
        {
        }

        private sealed class Capture(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}
