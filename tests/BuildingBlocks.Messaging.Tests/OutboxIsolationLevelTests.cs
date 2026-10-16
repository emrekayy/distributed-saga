using System.Data;
using BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>
/// ADR-020: MassTransit EF outbox varsayılanı RepeatableRead'dir. Pessimistic (FOR UPDATE) çalışan servisler
/// ReadCommitted vermelidir; varsayılan değişirse bu test, kararın dayanağının düştüğünü haber verir.
/// </summary>
public sealed class OutboxIsolationLevelTests
{
    [Fact]
    public void Outbox_default_isolation_is_repeatable_read()
    {
        Assert.Equal(IsolationLevel.RepeatableRead, ResolveIsolationLevel(outboxIsolationLevel: null));
    }

    [Fact]
    public void Outbox_isolation_can_be_overridden_to_read_committed()
    {
        Assert.Equal(IsolationLevel.ReadCommitted, ResolveIsolationLevel(IsolationLevel.ReadCommitted));
    }

    private static IsolationLevel ResolveIsolationLevel(IsolationLevel? outboxIsolationLevel)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RABBITMQ_USER"] = "u",
                ["RABBITMQ_PASSWORD"] = "p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseNpgsql("Host=localhost;Database=unused"));
        services.AddMessaging<TestDbContext>(configuration, outboxIsolationLevel: outboxIsolationLevel);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<EntityFrameworkOutboxOptions<TestDbContext>>>().Value.IsolationLevel;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}
