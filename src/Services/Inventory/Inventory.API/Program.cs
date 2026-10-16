using BuildingBlocks.Messaging.Configuration;
using BuildingBlocks.Web;
using Inventory.API.Endpoints;
using Inventory.API.Infrastructure;
using Inventory.Application;
using Inventory.Infrastructure;

await HealthProbe.RunIfRequestedAsync(args);

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Secret'lar repoda değil, kökteki .env'de (git'e girmez). Container'da ortam değişkenleri kullanılır.
    builder.Configuration.AddDotEnvFromParentDirectories(builder.Environment.ContentRootPath);
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddInventoryApplication();
builder.Services.AddInventoryInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Yalnızca Development: production'da migration ayrı bir deploy adımıdır (migration bundle, ADR-016/022).
    await app.Services.MigrateInventoryDatabaseAsync();
}

// Demo bayrakları (varsayılan kapalı): /internal/* tanı endpoint'leri ve demo verisi.
if (app.AreInternalEndpointsEnabled())
{
    app.MapInternalInventoryEndpoints();
}

if (app.Configuration.IsSeedEnabled())
{
    await app.Services.SeedInventoryDatabaseAsync();
}

app.MapProductEndpoints();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>WebApplicationFactory tabanlı entegrasyon testleri için.</summary>
public partial class Program;
