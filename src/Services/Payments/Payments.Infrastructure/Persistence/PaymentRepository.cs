using System.Data;
using Microsoft.EntityFrameworkCore;
using Payments.Application.Transactions;
using Payments.Application.Wallets;
using Payments.Domain.Transactions;
using Payments.Domain.Wallets;

namespace Payments.Infrastructure.Persistence;

internal sealed class PaymentRepository(PaymentDbContext db) : IPaymentRepository, IWalletRepository, IUnitOfWork
{
    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public async Task<Payment?> GetByOrderIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var locked = await db.Payments
            .FromSql($"""SELECT * FROM "Payments" WHERE "OrderId" = {orderId} FOR UPDATE""")
            .AsTracking()
            .ToListAsync(cancellationToken);
        return locked.SingleOrDefault();
    }

    public void Add(Payment payment) => db.Payments.Add(payment);

    public Task<Wallet?> GetAsync(Guid customerId, CancellationToken cancellationToken) =>
        db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.CustomerId == customerId, cancellationToken);

    public async Task<Wallet?> GetForUpdateAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var locked = await db.Wallets
            .FromSql($"""SELECT * FROM "Wallets" WHERE "CustomerId" = {customerId} FOR UPDATE""")
            .AsTracking()
            .ToListAsync(cancellationToken);
        return locked.SingleOrDefault();
    }

    public void Add(Wallet wallet) => db.Wallets.Add(wallet);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
