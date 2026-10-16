namespace Order.Application.Orders;

/// <summary>Aynı Idempotency-Key eşzamanlı bir istek tarafından zaten kaydedildi (unique index ihlali).</summary>
public sealed class DuplicateIdempotencyKeyException(string idempotencyKey, Exception innerException)
    : Exception($"An order with idempotency key '{idempotencyKey}' already exists.", innerException);

/// <summary>Idempotency-Key daha önce farklı bir istek gövdesiyle kullanılmış (HTTP 422).</summary>
public sealed class IdempotencyKeyConflictException(string idempotencyKey)
    : Exception($"Idempotency key '{idempotencyKey}' was already used with a different request body.");
