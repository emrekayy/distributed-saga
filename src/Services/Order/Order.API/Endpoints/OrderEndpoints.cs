using Microsoft.AspNetCore.Mvc;
using Order.Application.Orders;

namespace Order.API.Endpoints;

public static class OrderEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", SubmitAsync)
            .WithName("SubmitOrder")
            .Produces<OrderResponse>(StatusCodes.Status202Accepted)
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        orders.MapGet("/{id:guid}", GetAsync)
            .WithName("GetOrder")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// İlk istek: 202 Accepted + Location (saga asenkron ilerler). Aynı Idempotency-Key ve aynı body: 200 +
    /// mevcut sipariş. Aynı key, farklı body: 422.
    /// </summary>
    private static async Task<IResult> SubmitAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        SubmitOrderRequest request,
        SubmitOrderHandler handler,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [IdempotencyKeyHeader] = ["The Idempotency-Key header is required."] });
        }

        var command = new SubmitOrderCommand(
            request.CustomerId,
            request.Currency ?? string.Empty,
            (request.Items ?? []).Select(i => new SubmitOrderItem(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
            idempotencyKey);

        var result = await handler.HandleAsync(command, cancellationToken);
        var body = OrderResponse.From(result.Order);

        return result.Created
            ? Results.Accepted($"/orders/{body.Id}", body)
            : Results.Ok(body);
    }

    private static async Task<IResult> GetAsync(Guid id, IOrderRepository orders, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(id, cancellationToken);
        return order is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Order not found")
            : Results.Ok(OrderResponse.From(order));
    }

    /// <summary>
    /// Yalnızca Development: saga'nın iç durumu. Public API sözleşmesinin parçası değildir.
    /// </summary>
    public static IEndpointRouteBuilder MapInternalOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/orders/{id:guid}/saga", async (Guid id, IOrderSagaStateReader reader, CancellationToken ct) =>
                await reader.GetCurrentStateAsync(id, ct) is { } state
                    ? Results.Ok(new { orderId = id, currentState = state })
                    : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Saga instance not found"))
            .ExcludeFromDescription();

        return app;
    }
}
