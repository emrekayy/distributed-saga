using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Payments.Infrastructure.FaultInjection;

internal sealed class FaultInjectionOptionsValidator(IHostEnvironment environment) : IValidateOptions<FaultInjectionOptions>
{
    public ValidateOptionsResult Validate(string? name, FaultInjectionOptions options)
    {
        if (options.Enabled && !IsAllowed(environment))
        {
            return ValidateOptionsResult.Fail(
                $"{FaultInjectionOptions.SectionName}:Enabled is only allowed in Development or Testing (environment is '{environment.EnvironmentName}').");
        }

        if (options.TransientFailureRate is < 0 or > 1)
        {
            return ValidateOptionsResult.Fail($"{FaultInjectionOptions.SectionName}:TransientFailureRate must be between 0 and 1.");
        }

        if (options.MaxTransientFailuresPerMessage < 0 || options.Delay < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail($"{FaultInjectionOptions.SectionName}: counts and delays cannot be negative.");
        }

        return ValidateOptionsResult.Success;
    }

    internal static bool IsAllowed(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");
}
