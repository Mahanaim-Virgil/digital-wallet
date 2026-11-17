namespace DigitalWallet.Api.Domain;

/// <summary>Desteklenen para birimleri (ISO 4217 kodları). Kur dönüşümü kapsam dışı.</summary>
public static class Currencies
{
    public static readonly IReadOnlySet<string> Supported =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TRY", "USD", "EUR" };

    public static bool IsSupported(string? code) => code is not null && Supported.Contains(code);
}
