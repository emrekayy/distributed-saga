using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>MassTransit'in ActivitySource ve Meter adı (<c>MassTransit.Logging.DiagnosticHeaders.DefaultListenerName</c>).</summary>
    public const string MassTransitSourceName = "MassTransit";

    /// <summary>
    /// Ortak gözlemlenebilirlik: tracing (ASP.NET Core, Npgsql, MassTransit), temel metrikler ve yapılandırılmış log.
    /// <para>
    /// OTLP exporter yalnızca <c>OTEL_EXPORTER_OTLP_[TRACES_|METRICS_]ENDPOINT</c> tanımlıysa eklenir (adres/protokol standart
    /// OTEL_* ortam değişkenlerinden okunur); tanımlı değilse (yerel <c>dotnet run</c>, testler) hiçbir dışa gönderim yapılmaz.
    /// </para>
    /// <para>
    /// Log: Development dışında tek satır JSON; scope'lar (TraceId, SpanId, OrderId) her satıra eklenir.
    /// </para>
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var configuration = builder.Configuration;

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName));

        otel.WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddNpgsql()
                .AddSource(MassTransitSourceName);

            if (HasEndpoint(configuration, "TRACES"))
            {
                tracing.AddOtlpExporter(o => ConfigureOtlp(o, configuration, "TRACES"));
            }
        });

        otel.WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddMeter(MassTransitSourceName);

            if (HasEndpoint(configuration, "METRICS"))
            {
                metrics.AddOtlpExporter(o => ConfigureOtlp(o, configuration, "METRICS"));
            }
        });

        builder.Logging.Configure(o => o.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(o => o.IncludeScopes = true);
        }

        return builder;
    }

    /// <summary>
    /// Sinyale özel adres (<c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> / <c>..._METRICS_ENDPOINT</c>) ve protokol açıkça uygulanır:
    /// trace Jaeger'a, metrik Prometheus'a gittiği için adresler farklıdır. Sinyale özel adres yoksa SDK'nın standart
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> davranışı geçerlidir.
    /// </summary>
    private static void ConfigureOtlp(OtlpExporterOptions options, IConfiguration configuration, string signal)
    {
        if (configuration[$"OTEL_EXPORTER_OTLP_{signal}_ENDPOINT"] is { Length: > 0 } endpoint)
        {
            options.Endpoint = new Uri(endpoint);
        }

        if (configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] is { } protocol)
        {
            options.Protocol = protocol.Equals("grpc", StringComparison.OrdinalIgnoreCase)
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf;
        }
    }

    private static bool HasEndpoint(IConfiguration configuration, string signal) =>
        !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
        || !string.IsNullOrWhiteSpace(configuration[$"OTEL_EXPORTER_OTLP_{signal}_ENDPOINT"]);
}
