namespace Payments.Infrastructure.FaultInjection;

/// <summary>
/// Yalnızca Development/Testing: ProcessPayment endpoint'ine hata ve gecikme enjekte eder (Adım 5 hata testleri).
/// Production'da <see cref="Enabled"/> true ise uygulama başlamaz ve filtre hiç kaydedilmez.
/// </summary>
public sealed class FaultInjectionOptions
{
    public const string SectionName = "FaultInjection";

    public bool Enabled { get; set; }

    /// <summary>0..1: bir denemenin geçici hatayla (retry'ın yakalayacağı) başarısız olma olasılığı.</summary>
    public double TransientFailureRate { get; set; }

    /// <summary>Bir mesaj için enjekte edilecek en fazla geçici hata; retry limitinin altında tutulursa mesaj sonunda işlenir.</summary>
    public int MaxTransientFailuresPerMessage { get; set; } = 3;

    /// <summary>true ise her deneme retry'a girmeyen bir hatayla düşer; mesaj _error kuyruğuna gider.</summary>
    public bool PermanentFailure { get; set; }

    /// <summary>Her denemeden önce beklenir (yavaş Payments / geç ödeme senaryosu).</summary>
    public TimeSpan Delay { get; set; }
}
