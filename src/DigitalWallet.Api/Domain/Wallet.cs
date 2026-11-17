namespace DigitalWallet.Api.Domain;

/// <summary>
/// Tek para birimli cüzdan. Bakiye yalnızca domain metotlarıyla değişir (Aşama 2'de ledger üzerinden).
/// </summary>
public sealed class Wallet
{
    public const int OwnerNameMaxLength = 100;

    public Guid Id { get; private set; }
    public string OwnerName { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal Balance { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    // EF Core materyalizasyonu için.
    private Wallet() { }

    public static Wallet Open(string ownerName, string currency, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerName) || ownerName.Trim().Length > OwnerNameMaxLength)
            throw new ArgumentException($"Sahip adı boş olamaz ve {OwnerNameMaxLength} karakteri geçemez.", nameof(ownerName));

        if (!Currencies.IsSupported(currency))
            throw new ArgumentException($"Desteklenmeyen para birimi: {currency}", nameof(currency));

        return new Wallet
        {
            Id = Guid.CreateVersion7(nowUtc),
            OwnerName = ownerName.Trim(),
            Currency = currency.ToUpperInvariant(),
            Balance = 0m,
            CreatedAtUtc = nowUtc.ToUniversalTime(),
        };
    }
}
