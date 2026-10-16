namespace Ordering.Application.Orders;

/// <summary>Yalnızca Development'taki iç gözlem endpoint'i için; public API sözleşmesinin parçası değildir.</summary>
public interface IOrderSagaStateReader
{
    Task<string?> GetCurrentStateAsync(Guid orderId, CancellationToken cancellationToken);
}
