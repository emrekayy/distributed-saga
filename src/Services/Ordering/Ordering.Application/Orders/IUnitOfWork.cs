namespace Ordering.Application.Orders;

public interface IUnitOfWork
{
    /// <summary>
    /// Bekleyen değişiklikleri (sipariş + outbox mesajları) tek transaction'da kaydeder.
    /// Aynı Idempotency-Key ile eşzamanlı bir kayıt kazandıysa <see cref="DuplicateIdempotencyKeyException"/>
    /// fırlatır ve bekleyen değişiklikleri atar.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
