using Inventory.Domain;
using Inventory.Domain.Products;
using Inventory.Domain.Reservations;

namespace Inventory.UnitTests.Domain;

public sealed class ReservationTests
{
    private static readonly Guid ProductA = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");
    private static readonly Guid ProductB = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Dictionary<Guid, Product> Products(int a, int b) => new()
    {
        [ProductA] = new Product(ProductA, "A", a),
        [ProductB] = new Product(ProductB, "B", b),
    };

    [Fact]
    public void Reserve_decrements_every_item_when_all_are_available()
    {
        var products = Products(10, 5);

        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, 3), new(ProductB, 5)], products, Now);

        Assert.Equal(ReservationStatus.Reserved, reservation.Status);
        Assert.Equal(7, products[ProductA].AvailableQuantity);
        Assert.Equal(0, products[ProductB].AvailableQuantity);
    }

    [Fact]
    public void Reserve_is_all_or_nothing_when_one_item_is_short()
    {
        var products = Products(10, 1);

        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, 3), new(ProductB, 2)], products, Now);

        Assert.Equal(ReservationStatus.Rejected, reservation.Status);
        Assert.Equal(RejectionReason.InsufficientStock, reservation.Rejection);
        Assert.Equal(10, products[ProductA].AvailableQuantity);
        Assert.Equal(1, products[ProductB].AvailableQuantity);
    }

    [Fact]
    public void Reserve_rejects_unknown_product_without_touching_stock()
    {
        var products = Products(10, 5);

        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, 1), new(Guid.NewGuid(), 1)], products, Now);

        Assert.Equal(RejectionReason.ProductNotFound, reservation.Rejection);
        Assert.Equal(10, products[ProductA].AvailableQuantity);
    }

    [Fact]
    public void Reserve_combines_duplicate_lines_of_the_same_product()
    {
        var products = Products(4, 5);

        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, 3), new(ProductA, 2)], products, Now);

        Assert.Equal(RejectionReason.InsufficientStock, reservation.Rejection);
        Assert.Equal(4, products[ProductA].AvailableQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_rejects_non_positive_quantity_as_validation_error(int quantity)
    {
        var products = Products(10, 5);

        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, quantity)], products, Now);

        Assert.Equal(RejectionReason.ValidationError, reservation.Rejection);
        Assert.Equal(10, products[ProductA].AvailableQuantity);
    }

    [Fact]
    public void Reserve_rejects_empty_item_list_as_validation_error()
    {
        var reservation = Reservation.Create(Guid.NewGuid(), [], Products(1, 1), Now);

        Assert.Equal(RejectionReason.ValidationError, reservation.Rejection);
    }

    [Fact]
    public void Release_returns_stock_once_and_is_idempotent()
    {
        var products = Products(10, 5);
        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, 3), new(ProductB, 2)], products, Now);

        Assert.True(reservation.Release(products, Now));
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.Equal(10, products[ProductA].AvailableQuantity);
        Assert.Equal(5, products[ProductB].AvailableQuantity);

        Assert.False(reservation.Release(products, Now));
        Assert.Equal(10, products[ProductA].AvailableQuantity);
    }

    [Fact]
    public void Release_of_rejected_reservation_changes_nothing()
    {
        var products = Products(1, 1);
        var reservation = Reservation.Create(Guid.NewGuid(), [new(ProductA, 5)], products, Now);

        Assert.False(reservation.Release(products, Now));
        Assert.Equal(ReservationStatus.Rejected, reservation.Status);
        Assert.Equal(1, products[ProductA].AvailableQuantity);
    }

    [Fact]
    public void Product_stock_never_goes_negative()
    {
        var product = new Product(ProductA, "A", 1);

        Assert.Throws<InventoryDomainException>(() => product.Restock(0));
        Assert.Throws<InventoryDomainException>(() => new Product(ProductA, "A", -1));
        Assert.Equal(1, product.AvailableQuantity);
    }
}
