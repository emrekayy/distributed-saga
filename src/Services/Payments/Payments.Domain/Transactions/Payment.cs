using Payments.Domain.Wallets;

namespace Payments.Domain.Transactions;

/// <summary>
/// Sipariş başına tek ödeme kaydı (OrderId unique, iş anahtarı). Başarısız sonuçlar da (Failed) kaydedilir;
/// aynı OrderId ile tekrar gelen komut yeniden tahsilat yapmaz, kayıtlı sonucu yeniden yayınlar.
/// </summary>
public sealed class Payment
{
    private Payment()
    {
        Currency = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid CustomerId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    public FailureReason? Failure { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Fiilen iade edilen tutar (Refunded değilse 0).</summary>
    public decimal RefundedAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Tahsilat kararı. <paramref name="lockedWallet"/> çağıranın kilitlediği (FOR UPDATE) cüzdandır, yoksa null.
    /// Geçersiz tutar / para birimi uyuşmazlığı → Failed (ValidationError); cüzdan yok ya da bakiye yetersiz →
    /// Failed (Declined); aksi halde bakiyeden düşülür ve Completed olur.
    /// </summary>
    public static Payment Process(
        Guid id,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string currency,
        Wallet? lockedWallet,
        DateTimeOffset now)
    {
        var payment = new Payment
        {
            Id = id,
            OrderId = orderId,
            CustomerId = customerId,
            Amount = amount,
            Currency = currency,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (amount <= 0 || currency is not { Length: 3 })
        {
            return payment.Fail(FailureReason.ValidationError, "Amount must be greater than zero and currency a 3-letter code.");
        }

        if (lockedWallet is null)
        {
            return payment.Fail(FailureReason.Declined, $"No wallet found for customer {customerId}.");
        }

        if (!string.Equals(lockedWallet.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            return payment.Fail(FailureReason.ValidationError,
                $"Wallet currency is {lockedWallet.Currency}, payment currency is {currency}.");
        }

        if (!lockedWallet.CanAfford(amount))
        {
            return payment.Fail(FailureReason.Declined,
                $"Insufficient balance: requested {amount} {currency}, available {lockedWallet.Balance}.");
        }

        lockedWallet.Debit(amount);
        payment.Status = PaymentStatus.Completed;
        return payment;
    }

    /// <summary>
    /// Yalnızca Completed ise tahsil edilen tutarın tamamını cüzdana iade eder ve Refunded yapar. Failed ya da
    /// zaten Refunded ise hiçbir şeye dokunmaz. Fiilen iade edilen tutarı döner (yoksa 0).
    /// </summary>
    public decimal Refund(Wallet lockedWallet, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Completed)
        {
            return 0m;
        }

        lockedWallet.Credit(Amount);
        Status = PaymentStatus.Refunded;
        RefundedAmount = Amount;
        UpdatedAt = now;
        return Amount;
    }

    private Payment Fail(FailureReason failure, string reason)
    {
        Status = PaymentStatus.Failed;
        Failure = failure;
        Reason = reason;
        return this;
    }
}
