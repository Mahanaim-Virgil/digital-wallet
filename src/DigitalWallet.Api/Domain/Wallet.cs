namespace DigitalWallet.Api.Domain;

/// <summary>
/// Tek para birimli cüzdan. Bakiye yalnızca <see cref="LedgerTransaction"/> üzerinden değişir
/// (Debit/Credit internal; endpoint'ler bunları doğrudan çağırmaz).
/// </summary>
public sealed class Wallet
{
    public const int OwnerNameMaxLength = 100;

    public Guid Id { get; private set; }
    public string OwnerName { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public WalletType Type { get; private set; }
    public decimal Balance { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsSystem => Type == WalletType.System;

    // EF Core materyalizasyonu için.
    private Wallet() { }

    public static Wallet Open(string ownerName, string currency, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerName) || ownerName.Trim().Length > OwnerNameMaxLength)
            throw new ArgumentException($"Sahip adı boş olamaz ve {OwnerNameMaxLength} karakteri geçemez.", nameof(ownerName));

        if (!Currencies.IsSupported(currency))
            throw new ArgumentException($"Desteklenmeyen para birimi: {currency}", nameof(currency));

        var createdAt = Timestamps.ToDatabasePrecision(nowUtc);

        return new Wallet
        {
            Id = Guid.CreateVersion7(createdAt),
            OwnerName = ownerName.Trim(),
            Currency = currency.ToUpperInvariant(),
            Type = WalletType.Customer,
            Balance = 0m,
            CreatedAtUtc = createdAt,
        };
    }

    internal void Credit(decimal amount)
    {
        Money.EnsureValidAmount(amount);
        Balance += amount;
    }

    internal void Debit(decimal amount)
    {
        Money.EnsureValidAmount(amount);

        // Bu kontrol bellekteki bakiyeye bakar; doğru olması için cüzdan satırı kilitli yüklenmiş olmalı
        // (WalletLocking.LockWalletsAsync). DB'deki CHECK (balance >= 0) son savunmadır.
        if (!IsSystem && Balance < amount)
            throw new DomainException(DomainErrors.InsufficientFunds,
                $"Yetersiz bakiye. Bakiye: {Money.Normalize(Balance)} {Currency}, istenen: {Money.Normalize(amount)} {Currency}.");

        Balance -= amount;
    }
}
