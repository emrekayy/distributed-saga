using Payments.Domain.Wallets;

namespace Payments.Application.Wallets;

/// <summary>Demo: cüzdana bakiye ekler; cüzdan yoksa verilen para biriminde oluşturur.</summary>
public sealed class TopUpWalletHandler(IWalletRepository wallets, IUnitOfWork unitOfWork)
{
    public async Task<Wallet> HandleAsync(Guid customerId, decimal amount, string currency, CancellationToken cancellationToken)
    {
        Wallet? result = null;
        await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var wallet = await wallets.GetForUpdateAsync(customerId, ct);
            if (wallet is null)
            {
                wallet = new Wallet(customerId, currency, 0m);
                wallets.Add(wallet);
            }

            wallet.TopUp(amount, currency);
            await unitOfWork.SaveChangesAsync(ct);
            result = wallet;
        }, cancellationToken);

        return result!;
    }
}
