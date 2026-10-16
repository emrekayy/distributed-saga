namespace Order.Application.Saga;

/// <summary>
/// Saga'nın kendine schedule ettiği iç mesaj (Shared.Contracts'ta değil; başka servis görmez).
/// AwaitingPayment'a girerken schedule edilir, PaymentCompleted/PaymentFailed'de iptal edilir.
/// </summary>
public sealed record PaymentTimeoutExpired
{
    public required Guid OrderId { get; init; }
}
