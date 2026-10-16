using MassTransit;
using Microsoft.Extensions.Logging;
using Payments.Application.Wallets;
using Payments.Domain.Transactions;
using Shared.Contracts.Payments.Commands;
using Shared.Contracts.Payments.Events;

namespace Payments.Application.Transactions;

/// <summary>
/// Compensation: iş açısından başarısız olamaz. Completed ise tahsil edilen tutar iade edilir ve Refunded yapılır;
/// ödeme yoksa, Failed ya da zaten Refunded ise bakiyeye dokunulmaz. Her durumda PaymentRefunded yayınlanır
/// (Amount = fiilen iade edilen tutar, yoksa 0), aksi halde saga takılır. İade tutarı kayıtlı ödemenin tutarıdır;
/// komuttaki tutar yalnızca tutarsızlık logu için okunur.
/// </summary>
public sealed class RefundPaymentConsumer(
    IPaymentRepository payments,
    IWalletRepository wallets,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RefundPaymentConsumer> logger) : IConsumer<RefundPayment>
{
    public async Task Consume(ConsumeContext<RefundPayment> context)
    {
        var command = context.Message;
        var cancellationToken = context.CancellationToken;

        var payment = await payments.GetByOrderIdForUpdateAsync(command.OrderId, cancellationToken);
        var refunded = 0m;
        if (payment is { Status: PaymentStatus.Completed })
        {
            var wallet = await wallets.GetForUpdateAsync(payment.CustomerId, cancellationToken)
                ?? throw new InvalidOperationException($"Wallet {payment.CustomerId} of completed payment {payment.Id} is missing.");
            refunded = payment.Refund(wallet, clock.GetUtcNow());

            if (command.Amount != payment.Amount)
            {
                logger.LogWarning(
                    "RefundPayment for order {OrderId} asked for {Requested} but the recorded payment is {Recorded}; refunded the recorded amount",
                    command.OrderId, command.Amount, payment.Amount);
            }
        }
        else
        {
            logger.LogInformation(
                "RefundPayment for order {OrderId}: nothing to refund ({Status})",
                command.OrderId, payment?.Status.ToString() ?? "no payment");
        }

        await context.Publish(new PaymentRefunded
        {
            OrderId = command.OrderId,
            Amount = refunded,
            Currency = payment?.Currency ?? command.Currency,
            OccurredAt = clock.GetUtcNow(),
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
