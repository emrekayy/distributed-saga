using Payments.Domain.Wallets;

namespace Payments.Application.Wallets;

public interface IWalletRepository
{
    Task<Wallet?> GetAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Cüzdan satırını <c>SELECT … FOR UPDATE</c> ile kilitler; kilit çağıranın transaction'ı bitene kadar tutulur (ADR-020).</summary>
    Task<Wallet?> GetForUpdateAsync(Guid customerId, CancellationToken cancellationToken);

    void Add(Wallet wallet);
}
