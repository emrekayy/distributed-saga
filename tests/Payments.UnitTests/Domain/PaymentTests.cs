using Payments.Domain;
using Payments.Domain.Transactions;
using Payments.Domain.Wallets;

namespace Payments.UnitTests.Domain;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private static readonly Guid _customer = Guid.NewGuid();

    private static Payment Process(decimal amount, Wallet? wallet, string currency = "TRY") =>
        Payment.Process(Guid.NewGuid(), Guid.NewGuid(), _customer, amount, currency, wallet, _now);

    [Fact]
    public void Process_debits_balance_and_completes_when_funds_are_sufficient()
    {
        var wallet = new Wallet(_customer, "TRY", 500m);

        var payment = Process(300m, wallet);

        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(200m, wallet.Balance);
    }

    [Fact]
    public void Process_declines_without_touching_balance_when_funds_are_insufficient()
    {
        var wallet = new Wallet(_customer, "TRY", 100m);

        var payment = Process(300m, wallet);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(FailureReason.Declined, payment.Failure);
        Assert.Equal(100m, wallet.Balance);
    }

    [Fact]
    public void Process_declines_when_customer_has_no_wallet()
    {
        var payment = Process(10m, wallet: null);

        Assert.Equal(FailureReason.Declined, payment.Failure);
    }

    [Fact]
    public void Process_fails_validation_on_currency_mismatch_without_touching_balance()
    {
        var wallet = new Wallet(_customer, "TRY", 500m);

        var payment = Process(10m, wallet, currency: "USD");

        Assert.Equal(FailureReason.ValidationError, payment.Failure);
        Assert.Equal(500m, wallet.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Process_fails_validation_on_non_positive_amount(int amount)
    {
        var wallet = new Wallet(_customer, "TRY", 500m);

        var payment = Process(amount, wallet);

        Assert.Equal(FailureReason.ValidationError, payment.Failure);
        Assert.Equal(500m, wallet.Balance);
    }

    [Fact]
    public void Process_can_spend_exact_balance()
    {
        var wallet = new Wallet(_customer, "TRY", 300m);

        Assert.Equal(PaymentStatus.Completed, Process(300m, wallet).Status);
        Assert.Equal(0m, wallet.Balance);
    }

    [Fact]
    public void Refund_returns_the_charged_amount_once()
    {
        var wallet = new Wallet(_customer, "TRY", 500m);
        var payment = Process(300m, wallet);

        Assert.Equal(300m, payment.Refund(wallet, _now));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(500m, wallet.Balance);

        Assert.Equal(0m, payment.Refund(wallet, _now));
        Assert.Equal(500m, wallet.Balance);
    }

    [Fact]
    public void Refund_of_failed_payment_changes_nothing()
    {
        var wallet = new Wallet(_customer, "TRY", 100m);
        var payment = Process(300m, wallet);

        Assert.Equal(0m, payment.Refund(wallet, _now));
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(100m, wallet.Balance);
    }

    [Fact]
    public void Wallet_balance_never_goes_negative()
    {
        Assert.Throws<PaymentsDomainException>(() => new Wallet(_customer, "TRY", -1m));
        Assert.Throws<PaymentsDomainException>(() => new Wallet(_customer, "TRY", 1m).TopUp(0m, "TRY"));
    }
}
