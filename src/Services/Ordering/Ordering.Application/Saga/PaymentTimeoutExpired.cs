using MassTransit;

namespace Ordering.Application.Saga;

/// <summary>
/// Saga'nın kendine schedule ettiği iç mesaj (Shared.Contracts'ta değil; başka servis görmez).
/// AwaitingPayment'a girerken schedule edilir, PaymentCompleted/PaymentFailed'de iptal edilir.
/// <para>
/// Adı sabitlenmiştir (ADR-019): Quartz bekleyen job'larda mesaj tipini (URN) saklar; tip adı namespace'ten
/// türeseydi bir yeniden adlandırma bekleyen timeout'ları teslim edilemez hale getirirdi.
/// </para>
/// </summary>
[MessageUrn(MessageUrnValue)]
[EntityName(EntityNameValue)]
public sealed record PaymentTimeoutExpired
{
    public const string MessageUrnValue = "ordering:payment-timeout-expired";
    public const string EntityNameValue = "ordering.payment-timeout-expired";

    public required Guid OrderId { get; init; }
}
