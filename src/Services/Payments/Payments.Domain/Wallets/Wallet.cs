namespace Payments.Domain.Wallets;

public sealed class Wallet
{
    private Wallet()
    {
        Currency = string.Empty;
    }

    public Wallet(Guid customerId, string currency, decimal balance)
    {
        if (currency is not { Length: 3 })
        {
            throw new PaymentsDomainException("Currency must be a 3-letter ISO 4217 code.");
        }

        if (balance < 0)
        {
            throw new PaymentsDomainException("Balance cannot be negative.");
        }

        CustomerId = customerId;
        Currency = currency.ToUpperInvariant();
        Balance = balance;
    }

    public Guid CustomerId { get; private set; }

    public string Currency { get; private set; }

    public decimal Balance { get; private set; }

    public bool CanAfford(decimal amount) => amount > 0 && Balance >= amount;

    public void TopUp(decimal amount, string currency)
    {
        if (amount <= 0)
        {
            throw new PaymentsDomainException("Top-up amount must be greater than zero.");
        }

        if (!string.Equals(currency, Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentsDomainException($"Wallet currency is {Currency}, not {currency}.");
        }

        Balance += amount;
    }

    internal void Debit(decimal amount)
    {
        if (!CanAfford(amount))
        {
            throw new PaymentsDomainException($"Insufficient balance: requested {amount}, available {Balance}.");
        }

        Balance -= amount;
    }

    internal void Credit(decimal amount)
    {
        if (amount <= 0)
        {
            throw new PaymentsDomainException("Credit amount must be greater than zero.");
        }

        Balance += amount;
    }
}
