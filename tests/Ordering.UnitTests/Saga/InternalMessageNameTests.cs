using MassTransit;
using Ordering.Application.Saga;
using Shared.Contracts.Stock.Events;

namespace Ordering.UnitTests.Saga;

/// <summary>
/// Shared.Contracts dışındaki, schedule edilen/saklanan mesajların adları namespace'ten bağımsız sabittir (ADR-019).
/// </summary>
public sealed class InternalMessageNameTests : OrderSagaFixture
{
    [Fact]
    public void PaymentTimeoutExpired_has_pinned_exchange_name()
    {
        Assert.Equal(
            "ordering.payment-timeout-expired",
            Harness.Bus.Topology.Message<PaymentTimeoutExpired>().EntityName);
    }

    [Fact]
    public void PaymentTimeoutExpired_has_pinned_message_urn()
    {
        Assert.Equal(
            "urn:message:ordering:payment-timeout-expired",
            MessageUrn.ForTypeString<PaymentTimeoutExpired>());
    }

    [Fact]
    public void Shared_contract_names_still_follow_namespace()
    {
        // Contract'lar sabitlenmez; adları namespace + tip adından türer (namespace = wire contract).
        Assert.Equal(
            "urn:message:Shared.Contracts.Stock.Events:StockReserved",
            MessageUrn.ForTypeString<StockReserved>());
    }

    [Fact]
    public async Task Timeout_message_scheduled_by_saga_carries_pinned_urn()
    {
        var orderId = await SubmitOrderAsync();
        await PublishAndWaitAsync(new StockReserved { OrderId = orderId, OccurredAt = DateTimeOffset.UtcNow });
        await AssertStateAsync(orderId, Machine.AwaitingPayment);

        // Delayed scheduler mesajı saga kuyruğuna "send" eder; zarfdaki mesaj tipi sabit URN olmalı.
        await WaitUntilAsync(
            () => Harness.Sent.Select<PaymentTimeoutExpired>().Any(x => x.Context.Message.OrderId == orderId),
            "PaymentTimeoutExpired was not scheduled.");
        var sent = Harness.Sent.Select<PaymentTimeoutExpired>().First(x => x.Context.Message.OrderId == orderId);
        Assert.Contains("urn:message:ordering:payment-timeout-expired", sent.Context.SupportedMessageTypes);
    }
}
