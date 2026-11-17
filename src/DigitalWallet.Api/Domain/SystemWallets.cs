namespace DigitalWallet.Api.Domain;

/// <summary>Her para birimi için bir sistem cüzdanı. Migration ile sabit kimliklerle eklenir.</summary>
public static class SystemWallets
{
    public static readonly Guid Try = new("00000000-0000-0000-0000-000000000949"); // ISO 4217 sayısal: 949
    public static readonly Guid Usd = new("00000000-0000-0000-0000-000000000840");
    public static readonly Guid Eur = new("00000000-0000-0000-0000-000000000978");

    public static Guid ForCurrency(string currency) => currency.ToUpperInvariant() switch
    {
        "TRY" => Try,
        "USD" => Usd,
        "EUR" => Eur,
        _ => throw new ArgumentOutOfRangeException(nameof(currency), currency, "Sistem cüzdanı olmayan para birimi."),
    };
}
