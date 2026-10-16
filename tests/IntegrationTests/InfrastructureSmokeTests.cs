using IntegrationTests.Support;
using Shared.Contracts.Common;
using Shared.Contracts.Stock.Commands;
using Shared.Contracts.Stock.Events;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests;

[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class InfrastructureSmokeTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task Ordering_is_ready_with_postgres_and_rabbitmq()
    {
        var response = await fixture.OrderingClient.GetAsync("/health/ready");

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reserve_stock_goes_through_real_rabbitmq_to_inventory_and_back()
    {
        var productId = await fixture.SeedProductAsync(5);
        var orderId = Guid.NewGuid();

        await fixture.Probe.SendCommandAsync(
            new ReserveStock
            {
                OrderId = orderId,
                Items = [new OrderItemDto { ProductId = productId, Quantity = 2, UnitPrice = 10 }],
                RequestedAt = DateTimeOffset.UtcNow,
            },
            QueueNames.ReserveStock);

        await Eventually.UntilAsync(() => fixture.Probe.Of<StockReserved>(orderId).Count == 1, "StockReserved published");

        await using var db = fixture.CreateStockDb();
        var remaining = await db.Products.Where(p => p.Id == productId).Select(p => p.AvailableQuantity).SingleAsync();
        Assert.Equal(3, remaining);
    }
}
