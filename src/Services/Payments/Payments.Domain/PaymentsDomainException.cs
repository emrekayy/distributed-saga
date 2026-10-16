namespace Payments.Domain;

/// <summary>İş kuralı / girdi ihlali. Retry edilmez (tekrar denemek sonucu değiştirmez).</summary>
public sealed class PaymentsDomainException(string message) : Exception(message);
