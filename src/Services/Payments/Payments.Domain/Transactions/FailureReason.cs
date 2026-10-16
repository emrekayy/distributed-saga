namespace Payments.Domain.Transactions;

/// <summary>Domain'in kendi hata nedeni; wire contract koduna (ReasonCodes) Application katmanı eşler.</summary>
public enum FailureReason
{
    Declined,
    ValidationError,
}
