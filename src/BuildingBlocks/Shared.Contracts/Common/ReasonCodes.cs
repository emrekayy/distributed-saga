namespace Shared.Contracts.Common;

/// <summary>
/// Hata event'lerindeki ReasonCode alanının bilinen değerleri. Compensation ve raporlama
/// kararları serbest metin Reason'a göre değil, bu kodlara göre verilir.
/// </summary>
public static class ReasonCodes
{
    /// <summary>Kalemlerden en az biri için yeterli stok yok.</summary>
    public const string InsufficientStock = "INSUFFICIENT_STOCK";

    /// <summary>Kalemlerden en az biri stok kataloğunda yok.</summary>
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";

    /// <summary>Ödeme sağlayıcısı tahsilatı reddetti.</summary>
    public const string PaymentDeclined = "PAYMENT_DECLINED";

    /// <summary>Saga, ödeme sonucunu süresi içinde alamadı.</summary>
    public const string PaymentTimeout = "PAYMENT_TIMEOUT";

    /// <summary>Ödeme sırasında iş kuralı dışı, kalıcı bir hata oluştu.</summary>
    public const string PaymentError = "PAYMENT_ERROR";

    /// <summary>Mesaj içeriği geçersiz (adet, tutar, para birimi vb.).</summary>
    public const string ValidationError = "VALIDATION_ERROR";
}
