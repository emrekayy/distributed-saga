using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Payments.Application.Wallets;

namespace Payments.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<TopUpWalletHandler>();
        return services;
    }
}
