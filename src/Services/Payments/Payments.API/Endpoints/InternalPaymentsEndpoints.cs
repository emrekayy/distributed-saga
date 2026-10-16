using Payments.Application.Transactions;

namespace Payments.API.Endpoints;

public sealed record PaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string Status,
    string? Failure,
    string? Reason,
    decimal RefundedAmount);

/// <summary>Yalnızca Development: uçtan uca senaryolarda ödeme sonucunu göstermek için.</summary>
public static class InternalPaymentsEndpoints
{
    public static IEndpointRouteBuilder MapInternalPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/payments/{orderId:guid}", async (Guid orderId, IPaymentRepository payments, CancellationToken ct) =>
            await payments.GetByOrderIdAsync(orderId, ct) is { } p
                ? Results.Ok(new PaymentResponse(
                    p.Id, p.OrderId, p.CustomerId, p.Amount, p.Currency, p.Status.ToString(), p.Failure?.ToString(), p.Reason, p.RefundedAmount))
                : Results.NotFound());

        return app;
    }
}
