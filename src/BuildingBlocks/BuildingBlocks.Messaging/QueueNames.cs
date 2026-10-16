namespace BuildingBlocks.Messaging;

/// <summary>
/// Command'ların gönderildiği sabit kuyruk adları. Command point-to-point'tir: saga gönderir,
/// yalnızca sahibi olan servis bu kuyruğu dinler. Adlar wire contract'ın parçasıdır.
/// </summary>
public static class QueueNames
{
    public const string ReserveStock = "stock-reserve-stock";
    public const string ReleaseStock = "stock-release-stock";
    public const string ProcessPayment = "payment-process-payment";
    public const string RefundPayment = "payment-refund-payment";
}
