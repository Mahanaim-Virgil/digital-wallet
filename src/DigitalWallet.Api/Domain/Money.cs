namespace DigitalWallet.Api.Domain;

/// <summary>Tutar kuralları. Desteklenen para birimlerinin (TRY, USD, EUR) hepsi 2 ondalıklıdır.</summary>
public static class Money
{
    public const int Decimals = 2;

    /// <summary>Tek işlemde izin verilen en yüksek tutar (numeric(19,4) taşmasına karşı da güvenlik payı).</summary>
    public const decimal MaxAmount = 1_000_000m;

    public static bool IsValidAmount(decimal amount) =>
        amount > 0m && amount <= MaxAmount && decimal.Round(amount, Decimals) == amount;

    public static void EnsureValidAmount(decimal amount)
    {
        if (!IsValidAmount(amount))
            throw new DomainException(DomainErrors.InvalidAmount,
                $"Tutar 0'dan büyük, en fazla {MaxAmount} ve en fazla {Decimals} ondalıklı olmalı.");
    }

    /// <summary>
    /// Yanıtlarda tutarı hep 2 ondalıkla gösterir (0 → 0.00, 0.0000 → 0.00).
    /// `+ 0.00m` decimal'in ölçeğini en az 2 yapar; Round fazlasını atar.
    /// </summary>
    public static decimal Normalize(decimal amount) => decimal.Round(amount, Decimals) + 0.00m;
}
