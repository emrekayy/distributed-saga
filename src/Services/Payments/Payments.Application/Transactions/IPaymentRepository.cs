using Payments.Domain.Transactions;

namespace Payments.Application.Transactions;

public interface IPaymentRepository
{
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Ödeme satırını kilitler (<c>FOR UPDATE</c>): eşzamanlı iki refund tutarı iki kez iade edemez. Kilit sırası: ödeme, sonra cüzdan.</summary>
    Task<Payment?> GetByOrderIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken);

    void Add(Payment payment);
}
