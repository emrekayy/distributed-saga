using Inventory.Application.Reservations;

namespace Inventory.API.Endpoints;

public sealed record ReservationResponse(
    Guid OrderId,
    string Status,
    string? Rejection,
    string? Reason,
    IReadOnlyList<ReservationItemResponse> Items);

public sealed record ReservationItemResponse(Guid ProductId, int Quantity);

/// <summary>Yalnızca Development: uçtan uca senaryolarda rezervasyon sonucunu göstermek için.</summary>
public static class InternalInventoryEndpoints
{
    public static IEndpointRouteBuilder MapInternalInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/reservations/{orderId:guid}", async (Guid orderId, IReservationRepository reservations, CancellationToken ct) =>
            await reservations.GetByOrderIdAsync(orderId, ct) is { } r
                ? Results.Ok(new ReservationResponse(
                    r.OrderId,
                    r.Status.ToString(),
                    r.Rejection?.ToString(),
                    r.Reason,
                    r.Items.Select(i => new ReservationItemResponse(i.ProductId, i.Quantity)).ToList()))
                : Results.NotFound());

        return app;
    }
}
