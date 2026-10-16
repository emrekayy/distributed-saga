using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Payments.Infrastructure;
using Payments.Infrastructure.FaultInjection;

namespace IntegrationTests;

/// <summary>Hata enjeksiyonu Production'da kapalıdır: filtre kaydedilmez ve açmaya çalışmak ayarı geçersiz kılar. Altyapı gerekmez.</summary>
[Trait("Category", "Integration")]
public sealed class FaultInjectionProductionTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Enabling_fault_injection_outside_development_or_testing_is_rejected(string environment)
    {
        using var provider = Build(environment, enabled: true);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<FaultInjectionOptions>>().CurrentValue);
        Assert.Contains("only allowed in Development or Testing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    public void The_injection_filter_is_registered_only_in_development_and_testing(string environment, bool registered)
    {
        using var provider = Build(environment, enabled: false);

        Assert.Equal(registered, provider.GetService<FaultInjectionFilter>() is not null);
        Assert.False(provider.GetRequiredService<IOptionsMonitor<FaultInjectionOptions>>().CurrentValue.Enabled);
    }

    [Fact]
    public void Enabling_fault_injection_in_testing_is_accepted()
    {
        using var provider = Build("Testing", enabled: true);

        Assert.True(provider.GetRequiredService<IOptionsMonitor<FaultInjectionOptions>>().CurrentValue.Enabled);
    }

    private static ServiceProvider Build(string environment, bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FaultInjection:Enabled"] = enabled.ToString() })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environment));
        services.AddLogging();
        services.AddPaymentsInfrastructure(configuration, new FakeHostEnvironment(environment));
        return services.BuildServiceProvider();
    }

    private sealed class FakeHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Payments.API";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
