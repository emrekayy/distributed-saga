namespace Payments.Application.Wallets;

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Kilitlerin tutulması gereken, consumer dışı akışlar için açık (ReadCommitted) transaction.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);
}
