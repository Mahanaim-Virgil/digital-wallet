namespace DigitalWallet.Api.Domain;

public enum WalletType
{
    /// <summary>Kullanıcı cüzdanı. Bakiyesi eksiye düşemez.</summary>
    Customer,

    /// <summary>"Dış dünya" hesabı (para yatırmanın karşı tarafı). Eksiye düşebilir.</summary>
    System,
}
