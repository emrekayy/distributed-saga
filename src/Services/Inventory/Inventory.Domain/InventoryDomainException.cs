namespace Inventory.Domain;

/// <summary>İş kuralı / girdi ihlali. Retry edilmez (tekrar denemek sonucu değiştirmez).</summary>
public sealed class InventoryDomainException(string message) : Exception(message);
