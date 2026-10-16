using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging.Persistence;

/// <summary>
/// Para kolonları üç serviste de aynıdır: <c>numeric(19,4)</c>. Her DbContext
/// <c>ConfigureConventions</c> içinde <see cref="UseMoneyPrecision"/> çağırır; tek tek HasPrecision yazılmaz.
/// </summary>
public static class MoneyConventions
{
    public const int Precision = 19;
    public const int Scale = 4;

    public static ModelConfigurationBuilder UseMoneyPrecision(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(Precision, Scale);
        return configurationBuilder;
    }
}
