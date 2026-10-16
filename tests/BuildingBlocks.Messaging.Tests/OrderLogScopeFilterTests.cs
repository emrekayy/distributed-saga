using System.Collections.Concurrent;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Tests;

public sealed class OrderLogScopeFilterTests
{
    [Fact]
    public async Task Consumer_log_lines_carry_the_message_order_id_in_scope()
    {
        var logs = new ScopeCapturingProvider();
        await using var provider = new ServiceCollection()
            .AddLogging(b => b.AddProvider(logs))
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<ProbeConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UseConsumeFilter(typeof(OrderLogScopeFilter<>), context);
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var orderId = Guid.NewGuid();

        await harness.Bus.Publish(new OrderMessage { OrderId = orderId });
        await harness.Consumed.Any<OrderMessage>();

        var line = Assert.Single(logs.Lines, l => l.Message == "handled");
        Assert.Contains($"OrderId:{orderId}", line.Scopes);
    }

    [Fact]
    public async Task Messages_without_order_id_are_consumed_without_extra_scope()
    {
        var logs = new ScopeCapturingProvider();
        await using var provider = new ServiceCollection()
            .AddLogging(b => b.AddProvider(logs))
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<PlainConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UseConsumeFilter(typeof(OrderLogScopeFilter<>), context);
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new PlainMessage());
        await harness.Consumed.Any<PlainMessage>();

        var line = Assert.Single(logs.Lines, l => l.Message == "plain");
        Assert.DoesNotContain(line.Scopes, s => s.StartsWith("OrderId:", StringComparison.Ordinal));
    }

    public sealed class OrderMessage
    {
        public Guid OrderId { get; init; }
    }

    public sealed class PlainMessage;

    private sealed class ProbeConsumer(ILogger<ProbeConsumer> logger) : IConsumer<OrderMessage>
    {
        public Task Consume(ConsumeContext<OrderMessage> context)
        {
            logger.LogInformation("handled");
            return Task.CompletedTask;
        }
    }

    private sealed class PlainConsumer(ILogger<PlainConsumer> logger) : IConsumer<PlainMessage>
    {
        public Task Consume(ConsumeContext<PlainMessage> context)
        {
            logger.LogInformation("plain");
            return Task.CompletedTask;
        }
    }

    private sealed record Line(string Message, IReadOnlyList<string> Scopes);

    private sealed class ScopeCapturingProvider : ILoggerProvider
    {
        private static readonly AsyncLocal<ImmutableScopes?> _current = new();

        private readonly ConcurrentQueue<Line> _lines = new();

        public IReadOnlyCollection<Line> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

        public void Dispose()
        {
        }

        private sealed record ImmutableScopes(string Head, ImmutableScopes? Tail)
        {
            public IEnumerable<string> All()
            {
                for (var scope = this; scope is not null; scope = scope.Tail)
                {
                    yield return scope.Head;
                }
            }
        }

        private sealed class CapturingLogger(ConcurrentQueue<Line> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                var previous = _current.Value;
                _current.Value = new ImmutableScopes(state.ToString() ?? string.Empty, previous);
                return new Restore(previous);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Enqueue(new Line(formatter(state, exception), _current.Value?.All().ToList() ?? []));

            private sealed class Restore(ImmutableScopes? previous) : IDisposable
            {
                public void Dispose() => _current.Value = previous;
            }
        }
    }
}
