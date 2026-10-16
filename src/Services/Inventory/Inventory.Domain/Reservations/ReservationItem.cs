namespace Inventory.Domain.Reservations;

public sealed record ReservationItem(Guid ProductId, int Quantity);
