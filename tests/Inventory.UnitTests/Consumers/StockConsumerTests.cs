using Inventory.Domain.Reservations;
using Shared.Contracts.Common;
using Shared.Contracts.Stock.Events;

namespace Inventory.UnitTests.Consumers;

public sealed class StockConsumerTests : StockConsumerFixture
{
    [Fact]
    public async Task Reserve_decrements_stock_and_publishes_StockReserved()
    {
        var orderId = Guid.NewGuid();

        await ReserveAsync(orderId, (Keyboard, 2));

        Assert.Equal(8, Store.Product(Keyboard).AvailableQuantity);
        Assert.Equal(ReservationStatus.Reserved, Store.Reservation(orderId)!.Status);
        Assert.Equal(1, PublishedCount<StockReserved>(orderId));
    }

    [Fact]
    public async Task Duplicate_reserve_does_not_decrement_again_and_republishes_same_success()
    {
        var orderId = Guid.NewGuid();

        await ReserveAsync(orderId, (Keyboard, 2));
        await ReserveAsync(orderId, (Keyboard, 2));

        Assert.Equal(8, Store.Product(Keyboard).AvailableQuantity);
        Assert.Equal(2, PublishedCount<StockReserved>(orderId));
        Assert.Equal(0, PublishedCount<StockReservationFailed>(orderId));
    }

    [Fact]
    public async Task Duplicate_reserve_republishes_recorded_failure_even_if_stock_arrived_meanwhile()
    {
        var orderId = Guid.NewGuid();

        await ReserveAsync(orderId, (Monitor, 1));
        Store.Product(Monitor).Restock(50);
        await ReserveAsync(orderId, (Monitor, 1));

        Assert.Equal(50, Store.Product(Monitor).AvailableQuantity);
        Assert.Equal(2, PublishedCount<StockReservationFailed>(orderId));
        Assert.Equal(0, PublishedCount<StockReserved>(orderId));
        Assert.Equal(ReasonCodes.InsufficientStock, LastPublished<StockReservationFailed>(orderId).ReasonCode);
    }

    [Fact]
    public async Task Reserve_of_unknown_product_publishes_PRODUCT_NOT_FOUND()
    {
        var orderId = Guid.NewGuid();

        await ReserveAsync(orderId, (Guid.NewGuid(), 1));

        Assert.Equal(ReasonCodes.ProductNotFound, LastPublished<StockReservationFailed>(orderId).ReasonCode);
    }

    [Fact]
    public async Task Reserve_with_no_items_publishes_VALIDATION_ERROR_instead_of_faulting()
    {
        var orderId = Guid.NewGuid();

        await ReserveAsync(orderId);

        Assert.Equal(ReasonCodes.ValidationError, LastPublished<StockReservationFailed>(orderId).ReasonCode);
    }

    [Fact]
    public async Task Release_returns_stock_and_publishes_StockReleased()
    {
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (Keyboard, 4));

        await ReleaseAsync(orderId);

        Assert.Equal(10, Store.Product(Keyboard).AvailableQuantity);
        Assert.Equal(ReservationStatus.Released, Store.Reservation(orderId)!.Status);
        Assert.Equal(1, PublishedCount<StockReleased>(orderId));
    }

    [Fact]
    public async Task Release_without_reservation_still_publishes_StockReleased()
    {
        var orderId = Guid.NewGuid();

        await ReleaseAsync(orderId);

        Assert.Equal(10, Store.Product(Keyboard).AvailableQuantity);
        Assert.Equal(1, PublishedCount<StockReleased>(orderId));
    }

    [Fact]
    public async Task Release_of_rejected_reservation_changes_nothing_but_publishes()
    {
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (Monitor, 1));

        await ReleaseAsync(orderId);

        Assert.Equal(0, Store.Product(Monitor).AvailableQuantity);
        Assert.Equal(ReservationStatus.Rejected, Store.Reservation(orderId)!.Status);
        Assert.Equal(1, PublishedCount<StockReleased>(orderId));
    }

    [Fact]
    public async Task Duplicate_release_does_not_return_stock_twice()
    {
        var orderId = Guid.NewGuid();
        await ReserveAsync(orderId, (Keyboard, 4));

        await ReleaseAsync(orderId);
        await ReleaseAsync(orderId);

        Assert.Equal(10, Store.Product(Keyboard).AvailableQuantity);
        Assert.Equal(2, PublishedCount<StockReleased>(orderId));
    }
}
