using Payments.Application.Wallets;
using Payments.Domain.Wallets;

namespace Payments.API.Endpoints;

public sealed record WalletResponse(Guid CustomerId, string Currency, decimal Balance)
{
    public static WalletResponse From(Wallet w) => new(w.CustomerId, w.Currency, w.Balance);
}

public sealed record TopUpRequest(decimal Amount, string Currency);

public static class WalletEndpoints
{
    public static IEndpointRouteBuilder MapWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wallets");

        group.MapGet("/{customerId:guid}", async (Guid customerId, IWalletRepository wallets, CancellationToken ct) =>
            await wallets.GetAsync(customerId, ct) is { } wallet
                ? Results.Ok(WalletResponse.From(wallet))
                : Results.NotFound());

        // Demo
        group.MapPost("/{customerId:guid}/top-up", async (Guid customerId, TopUpRequest request, TopUpWalletHandler handler, CancellationToken ct) =>
            Results.Ok(WalletResponse.From(await handler.HandleAsync(customerId, request.Amount, request.Currency, ct))));

        return app;
    }
}
