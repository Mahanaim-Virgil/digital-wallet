namespace DigitalWallet.Api.Domain;

/// <summary>Her para birimi için bir sistem cüzdanı. Migration ile sabit kimliklerle eklenir.</summary>
public static class SystemWallets
{
    public static readonly Guid Try = new("00000000-0000-0000-0000-000000000949"); // ISO 4217 sayısal: 949
    public static readonly Guid Usd = new("00000000-0000-0000-0000-000000000840");
    public static readonly Guid Eur = new("00000000-0000-0000-0000-000000000978");
}
