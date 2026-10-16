using BuildingBlocks.Messaging.Configuration;
using BuildingBlocks.Observability;
using BuildingBlocks.Web;
using Payments.API.Endpoints;
using Payments.API.Infrastructure;
using Payments.Application;
using Payments.Infrastructure;

await HealthProbe.RunIfRequestedAsync(args);

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Secret'lar repoda değil, kökteki .env'de (git'e girmez). Container'da ortam değişkenleri kullanılır.
    builder.Configuration.AddDotEnvFromParentDirectories(builder.Environment.ContentRootPath);
}

builder.AddObservability("payments");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddPaymentsApplication();
builder.Services.AddPaymentsInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnızca Development: production'da migration ayrı bir deploy adımıdır (migration bundle, ADR-016/022).
    await app.Services.MigratePaymentDatabaseAsync();
}

// Demo bayrakları (varsayılan kapalı): /internal/* tanı endpoint'leri ve demo verisi.
if (app.AreInternalEndpointsEnabled())
{
    app.MapInternalPaymentsEndpoints();
}

if (app.Configuration.IsSeedEnabled())
{
    await app.Services.SeedPaymentDatabaseAsync();
}

app.MapWalletEndpoints();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>WebApplicationFactory tabanlı entegrasyon testleri için.</summary>
public partial class Program;
