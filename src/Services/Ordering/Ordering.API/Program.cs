using BuildingBlocks.Messaging.Configuration;
using BuildingBlocks.Observability;
using BuildingBlocks.Web;
using Ordering.API.Endpoints;
using Ordering.API.Infrastructure;
using Ordering.Application;
using Ordering.Infrastructure;

await HealthProbe.RunIfRequestedAsync(args);

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Secret'lar repoda değil, kökteki .env'de (git'e girmez). Container'da ortam değişkenleri kullanılır.
    builder.Configuration.AddDotEnvFromParentDirectories(builder.Environment.ContentRootPath);
}

builder.AddObservability("ordering");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddOrderApplication();
builder.Services.AddOrderInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnızca Development: production'da migration ayrı bir deploy adımıdır (migration bundle, ADR-016/022).
    await app.Services.MigrateOrderDatabaseAsync();
}

// Demo bayrakları (varsayılan kapalı): /internal/* tanı endpoint'leri ve demo verisi.
if (app.AreInternalEndpointsEnabled())
{
    app.MapInternalOrderEndpoints();
}

app.MapOrderEndpoints();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>WebApplicationFactory tabanlı entegrasyon testleri için.</summary>
public partial class Program;
