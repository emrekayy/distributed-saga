using MassTransit;
using Microsoft.Extensions.Logging;
using Payments.Application.Wallets;
using Payments.Domain.Transactions;
using Shared.Contracts.Common;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;

namespace Payments.Application.Transactions;

/// <summary>
/// İş anahtarı OrderId: kayıt varsa yeniden tahsilat yapılmaz, kayıtlı sonuç (başarılı ya da başarısız) yeniden
/// yayınlanır. Yoksa cüzdan satırı kilitlenir, karar verilir, sonuç kaydedilir; event outbox üzerinden aynı
/// SaveChanges'te yazılır. Aynı OrderId ile eşzamanlı gelen ikinci insert 23505 alır (bakiye düşüşü geri alınır),
/// retry'da kayıtlı sonucu bulur.
/// </summary>
public sealed class ProcessPaymentConsumer(
    IPaymentRepository payments,
    IWalletRepository wallets,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ProcessPaymentConsumer> logger) : IConsumer<ProcessPayment>
{
    public async Task Consume(ConsumeContext<ProcessPayment> context)
    {
        var command = context.Message;
        var cancellationToken = context.CancellationToken;

        var payment = await payments.GetByOrderIdAsync(command.OrderId, cancellationToken);
        if (payment is null)
        {
            var wallet = await wallets.GetForUpdateAsync(command.CustomerId, cancellationToken);
            payment = Payment.Process(
                NewId.NextGuid(), command.OrderId, command.CustomerId, command.Amount, command.Currency, wallet, clock.GetUtcNow());
            payments.Add(payment);
            logger.LogInformation(
                "Payment for order {OrderId} recorded as {Status} ({Failure})", command.OrderId, payment.Status, payment.Failure);
        }
        else
        {
            logger.LogInformation(
                "Duplicate ProcessPayment for order {OrderId}; republishing recorded result {Status}", command.OrderId, payment.Status);
        }

        await PublishResultAsync(context, payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Refunded bir ödemenin asıl sonucu da başarıydı; saga Compensating/Failed'de PaymentCompleted'ı refund ile karşılar.</summary>
    private Task PublishResultAsync(ConsumeContext context, Payment payment) =>
        payment.Status == PaymentStatus.Failed
            ? context.Publish(new PaymentFailed
            {
                OrderId = payment.OrderId,
                Reason = payment.Reason ?? "Payment failed.",
                ReasonCode = payment.Failure == FailureReason.Declined ? ReasonCodes.PaymentDeclined : ReasonCodes.ValidationError,
                OccurredAt = clock.GetUtcNow(),
            })
            : context.Publish(new PaymentCompleted
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                Amount = payment.Amount,
                Currency = payment.Currency,
                OccurredAt = clock.GetUtcNow(),
            });
}
