namespace Ordering.Application.Saga;

public sealed class OrderSagaOptions
{
    public const string SectionName = "OrderSaga";

    /// <summary>
    /// AwaitingPayment'ta beklenecek en uzun süre. Aynı değer ProcessPayment'ın TimeToLive'ıdır:
    /// süre dolunca bekleyen bayat komut Payment'a hiç ulaşmaz.
    /// </summary>
    public TimeSpan PaymentTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
