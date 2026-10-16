using BuildingBlocks.Messaging.Configuration;
using BuildingBlocks.Web;
using Payments.API.Endpoints;
using Payments.API.Infrastructure;
using Payments.Application;
using Payments.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Secret'lar repoda değil, kökteki .env'de (git'e girmez). Container'da ortam değişkenleri kullanılır.
    builder.Configuration.AddDotEnvFromParentDirectories(builder.Environment.ContentRootPath);
}

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddPaymentsApplication();
builder.Services.AddPaymentsInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapInternalPaymentsEndpoints();

    // Yalnızca Development: production'da migration ayrı bir deploy adımıdır.
    await app.Services.MigrateAndSeedPaymentDatabaseAsync();
}

app.MapWalletEndpoints();
app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>WebApplicationFactory tabanlı entegrasyon testleri için.</summary>
public partial class Program;
