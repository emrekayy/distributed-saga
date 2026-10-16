using Payments.Domain.Transactions;
using Shared.Contracts.Common;
using Shared.Contracts.Payments.Events;

namespace Payments.UnitTests.Consumers;

public sealed class PaymentConsumerTests : PaymentConsumerFixture
{
    [Fact]
    public async Task Process_debits_wallet_and_publishes_PaymentCompleted_with_PaymentId()
    {
        var orderId = Guid.NewGuid();

        await ProcessAsync(orderId, RichCustomer, 300m);

        var completed = LastPublished<PaymentCompleted>(orderId);
        Assert.Equal(700m, Store.Wallet(RichCustomer).Balance);
        Assert.Equal(Store.Payment(orderId)!.Id, completed.PaymentId);
        Assert.Equal(300m, completed.Amount);
        Assert.Equal("TRY", completed.Currency);
    }

    [Fact]
    public async Task Duplicate_process_does_not_charge_again_and_republishes_same_payment()
    {
        var orderId = Guid.NewGuid();

        await ProcessAsync(orderId, RichCustomer, 300m);
        await ProcessAsync(orderId, RichCustomer, 300m);

        Assert.Equal(700m, Store.Wallet(RichCustomer).Balance);
        Assert.Equal(1, Store.PaymentCount);
        Assert.Equal(2, PublishedCount<PaymentCompleted>(orderId));
        Assert.Equal(0, PublishedCount<PaymentFailed>(orderId));
    }

    [Fact]
    public async Task Insufficient_balance_publishes_PAYMENT_DECLINED_and_keeps_balance()
    {
        var orderId = Guid.NewGuid();

        await ProcessAsync(orderId, PoorCustomer, 300m);

        Assert.Equal(100m, Store.Wallet(PoorCustomer).Balance);
        Assert.Equal(ReasonCodes.PaymentDeclined, LastPublished<PaymentFailed>(orderId).ReasonCode);
    }

    [Fact]
    public async Task Duplicate_process_republishes_recorded_failure_even_if_balance_arrived_meanwhile()
    {
        var orderId = Guid.NewGuid();

        await ProcessAsync(orderId, PoorCustomer, 300m);
        Store.Wallet(PoorCustomer).TopUp(1000m, "TRY");
        await ProcessAsync(orderId, PoorCustomer, 300m);

        Assert.Equal(1100m, Store.Wallet(PoorCustomer).Balance);
        Assert.Equal(2, PublishedCount<PaymentFailed>(orderId));
        Assert.Equal(0, PublishedCount<PaymentCompleted>(orderId));
    }

    [Fact]
    public async Task Currency_mismatch_publishes_VALIDATION_ERROR()
    {
        var orderId = Guid.NewGuid();

        await ProcessAsync(orderId, RichCustomer, 10m, currency: "USD");

        Assert.Equal(ReasonCodes.ValidationError, LastPublished<PaymentFailed>(orderId).ReasonCode);
        Assert.Equal(1000m, Store.Wallet(RichCustomer).Balance);
    }

    [Fact]
    public async Task Unknown_customer_is_declined()
    {
        var orderId = Guid.NewGuid();

        await ProcessAsync(orderId, Guid.NewGuid(), 10m);

        Assert.Equal(ReasonCodes.PaymentDeclined, LastPublished<PaymentFailed>(orderId).ReasonCode);
    }

    [Fact]
    public async Task Refund_returns_money_and_publishes_actual_amount()
    {
        var orderId = Guid.NewGuid();
        await ProcessAsync(orderId, RichCustomer, 300m);

        await RefundAsync(orderId, 300m);

        Assert.Equal(1000m, Store.Wallet(RichCustomer).Balance);
        Assert.Equal(PaymentStatus.Refunded, Store.Payment(orderId)!.Status);
        Assert.Equal(300m, LastPublished<PaymentRefunded>(orderId).Amount);
    }

    [Fact]
    public async Task Refund_without_payment_still_publishes_PaymentRefunded_with_zero()
    {
        var orderId = Guid.NewGuid();

        await RefundAsync(orderId, 300m);

        var refunded = LastPublished<PaymentRefunded>(orderId);
        Assert.Equal(0m, refunded.Amount);
        Assert.Equal("TRY", refunded.Currency);
        Assert.Equal(1000m, Store.Wallet(RichCustomer).Balance);
    }

    [Fact]
    public async Task Refund_of_failed_payment_changes_nothing_but_publishes()
    {
        var orderId = Guid.NewGuid();
        await ProcessAsync(orderId, PoorCustomer, 300m);

        await RefundAsync(orderId, 300m);

        Assert.Equal(100m, Store.Wallet(PoorCustomer).Balance);
        Assert.Equal(PaymentStatus.Failed, Store.Payment(orderId)!.Status);
        Assert.Equal(0m, LastPublished<PaymentRefunded>(orderId).Amount);
    }

    [Fact]
    public async Task Duplicate_refund_does_not_credit_twice_and_second_event_has_zero_amount()
    {
        var orderId = Guid.NewGuid();
        await ProcessAsync(orderId, RichCustomer, 300m);

        await RefundAsync(orderId, 300m);
        await RefundAsync(orderId, 300m);

        Assert.Equal(1000m, Store.Wallet(RichCustomer).Balance);
        Assert.Equal(2, PublishedCount<PaymentRefunded>(orderId));
        Assert.Equal(0m, LastPublished<PaymentRefunded>(orderId).Amount);
    }
}
