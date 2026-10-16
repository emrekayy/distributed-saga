using BuildingBlocks.Messaging.Configuration;
using BuildingBlocks.Web;
using Ordering.API.Endpoints;
using Ordering.API.Infrastructure;
using Ordering.Application;
using Ordering.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Secret'lar repoda değil, kökteki .env'de (git'e girmez). Container'da ortam değişkenleri kullanılır.
    builder.Configuration.AddDotEnvFromParentDirectories(builder.Environment.ContentRootPath);
}

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
    app.MapInternalOrderEndpoints();

    // Yalnızca Development: production'da migration ayrı bir deploy adımıdır.
    await app.Services.MigrateOrderDatabaseAsync();
}

app.MapOrderEndpoints();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>WebApplicationFactory tabanlı entegrasyon testleri için.</summary>
public partial class Program;
