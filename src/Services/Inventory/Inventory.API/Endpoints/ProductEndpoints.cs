using Inventory.Application.Products;
using Inventory.Domain.Products;

namespace Inventory.API.Endpoints;

public sealed record ProductResponse(Guid Id, string Name, int AvailableQuantity)
{
    public static ProductResponse From(Product p) => new(p.Id, p.Name, p.AvailableQuantity);
}

public sealed record RestockRequest(int Quantity);

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products");

        group.MapGet("/", async (IProductRepository products, CancellationToken ct) =>
            Results.Ok((await products.ListAsync(ct)).Select(ProductResponse.From)));

        group.MapGet("/{id:guid}", async (Guid id, IProductRepository products, CancellationToken ct) =>
            await products.GetAsync(id, ct) is { } product
                ? Results.Ok(ProductResponse.From(product))
                : Results.NotFound());

        // Demo/admin
        group.MapPost("/{id:guid}/restock", async (Guid id, RestockRequest request, RestockProductHandler handler, CancellationToken ct) =>
            await handler.HandleAsync(id, request.Quantity, ct) is { } product
                ? Results.Ok(ProductResponse.From(product))
                : Results.NotFound());

        return app;
    }
}
