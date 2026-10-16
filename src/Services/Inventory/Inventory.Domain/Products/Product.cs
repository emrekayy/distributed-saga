namespace Inventory.Domain.Products;

public sealed class Product
{
    public const int MaxNameLength = 200;

    private Product()
    {
        Name = string.Empty;
    }

    public Product(Guid id, string name, int availableQuantity)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            throw new InventoryDomainException($"Product name is required and at most {MaxNameLength} characters.");
        }

        if (availableQuantity < 0)
        {
            throw new InventoryDomainException("Available quantity cannot be negative.");
        }

        Id = id;
        Name = name;
        AvailableQuantity = availableQuantity;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public int AvailableQuantity { get; private set; }

    public bool CanFulfil(int quantity) => quantity > 0 && AvailableQuantity >= quantity;

    public void Restock(int quantity)
    {
        EnsurePositive(quantity);
        AvailableQuantity = checked(AvailableQuantity + quantity);
    }

    /// <summary>Rezervasyon için stoktan düşer; yetersizse hiçbir şey değiştirmeden fırlatır.</summary>
    internal void Take(int quantity)
    {
        EnsurePositive(quantity);
        if (AvailableQuantity < quantity)
        {
            throw new InventoryDomainException(
                $"Insufficient stock for product {Id}: requested {quantity}, available {AvailableQuantity}.");
        }

        AvailableQuantity -= quantity;
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InventoryDomainException("Quantity must be greater than zero.");
        }
    }
}
