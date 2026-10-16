using MassTransit;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Stock.Commands;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Command tiplerini kuyruklarına eşler; saga <c>Send</c>'i adres vermeden çağırabilir ve
/// Application katmanı kuyruk adlarını bilmek zorunda kalmaz.
/// </summary>
public static class CommandEndpoints
{
    private static int _mapped;

    /// <summary>Process başına bir kez çalışır (EndpointConvention global'dir).</summary>
    public static void Map()
    {
        if (Interlocked.Exchange(ref _mapped, 1) == 1)
        {
            return;
        }

        EndpointConvention.Map<ReserveStock>(Address(QueueNames.ReserveStock));
        EndpointConvention.Map<ReleaseStock>(Address(QueueNames.ReleaseStock));
        EndpointConvention.Map<ProcessPayment>(Address(QueueNames.ProcessPayment));
        EndpointConvention.Map<RefundPayment>(Address(QueueNames.RefundPayment));
    }

    public static Uri Address(string queueName) => new($"queue:{queueName}");
}
