using Inventory.Domain.Reservations;

namespace Inventory.Application.Reservations;

public interface IReservationRepository
{
    Task<Reservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Rezervasyon satırını kilitler (<c>FOR UPDATE</c>): eşzamanlı iki release stoğu iki kez iade edemez.</summary>
    Task<Reservation?> GetByOrderIdForUpdateAsync(Guid orderId, CancellationToken cancellationToken);

    void Add(Reservation reservation);
}
