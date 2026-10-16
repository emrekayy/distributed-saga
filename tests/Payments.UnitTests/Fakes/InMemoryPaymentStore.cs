using Payments.Application.Transactions;
using Payments.Application.Wallets;
using Payments.Domain.Transactions;
using Payments.Domain.Wallets;

namespace Payments.UnitTests.Fakes;

/// <summary>Repository arayüzlerinin bellek içi karşılığı; "kilit" gerçek DB testlerinde (Adım 5) doğrulanır.</summary>
public sealed class InMemoryPaymentStore : IPaymentRepository, IWalletRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Wallet> _wallets = [];
    private readonly Dictionary<Guid, Payment> _payments = [];

    public Wallet AddWallet(Guid customerId, string currency, decimal balance)
    {
        var wallet = new Wallet(customerId, currency, balance);
        _wallets[customerId] = wallet;
        return wallet;
    }

    public Wallet Wallet(Guid customerId) => _wallets[customerId];

    public Payment? Payment(Guid orderId) => _payments.GetValueOrDefault(orderId);

    public int PaymentCount => _payments.Count;

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(_payments.GetValueOrDefault(orderId));

    public Task<Payment?> GetByOrderIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken) =>
        GetByOrderIdAsync(orderId, cancellationToken);

    public void Add(Payment payment) => _payments.Add(payment.OrderId, payment);

    public Task<Wallet?> GetAsync(Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_wallets.GetValueOrDefault(customerId));

    public Task<Wallet?> GetForUpdateAsync(Guid customerId, CancellationToken cancellationToken) =>
        GetAsync(customerId, cancellationToken);

    public void Add(Wallet wallet) => _wallets.Add(wallet.CustomerId, wallet);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        action(cancellationToken);
}
