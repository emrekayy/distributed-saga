namespace Inventory.Domain.Reservations;

/// <summary>Domain'in kendi ret nedeni; wire contract koduna (ReasonCodes) Application katmanı eşler.</summary>
public enum RejectionReason
{
    ValidationError,
    ProductNotFound,
    InsufficientStock,
}
