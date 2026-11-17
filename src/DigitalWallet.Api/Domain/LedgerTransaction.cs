namespace DigitalWallet.Api.Domain;

public enum TransactionType
{
    Deposit,
    Transfer,
}

/// <summary>
/// Bir para hareketi: en az iki kayıt, kayıtların toplamı her zaman 0 (çift taraflı kayıt).
/// Kayıtları ve bakiye değişikliklerini oluşturan tek yer burasıdır.
/// </summary>
public sealed class LedgerTransaction
{
    private readonly List<LedgerEntry> _entries = [];

    public Guid Id { get; private set; }
    public TransactionType Type { get; private set; }
    public string Currency { get; private set; } = null!;

    /// <summary>Hareket eden tutar (her zaman pozitif).</summary>
    public decimal Amount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    private LedgerTransaction() { }

    /// <summary>Dışarıdan para girişi: sistem cüzdanı (−) → müşteri cüzdanı (+).</summary>
    public static LedgerTransaction Deposit(Wallet systemWallet, Wallet target, decimal amount, DateTimeOffset nowUtc)
    {
        if (!systemWallet.IsSystem)
            throw new InvalidOperationException("Para yatırmanın kaynağı sistem cüzdanı olmalı.");

        if (target.IsSystem)
            throw new DomainException(DomainErrors.SystemWalletNotAllowed, "Sistem cüzdanına para yatırılamaz.");

        return Post(TransactionType.Deposit, systemWallet, target, amount, nowUtc);
    }

    /// <summary>Müşteriden müşteriye transfer: gönderen (−) → alıcı (+).</summary>
    public static LedgerTransaction Transfer(Wallet from, Wallet to, decimal amount, DateTimeOffset nowUtc)
    {
        if (from.Id == to.Id)
            throw new DomainException(DomainErrors.SameWallet, "Gönderen ve alıcı cüzdan aynı olamaz.");

        if (from.IsSystem || to.IsSystem)
            throw new DomainException(DomainErrors.SystemWalletNotAllowed, "Sistem cüzdanı transferde kullanılamaz.");

        return Post(TransactionType.Transfer, from, to, amount, nowUtc);
    }

    private static LedgerTransaction Post(TransactionType type, Wallet from, Wallet to, decimal amount, DateTimeOffset nowUtc)
    {
        Money.EnsureValidAmount(amount);

        if (from.Currency != to.Currency)
            throw new DomainException(DomainErrors.CurrencyMismatch,
                $"Para birimleri farklı: {from.Currency} → {to.Currency}. Kur dönüşümü desteklenmiyor.");

        var createdAt = Timestamps.ToDatabasePrecision(nowUtc);

        // Önce bakiyeler değişir (Debit yetersiz bakiyede istisna atar), sonra kayıtlar
        // yeni bakiyeyi BalanceAfter olarak yakalar.
        from.Debit(amount);
        to.Credit(amount);

        var transaction = new LedgerTransaction
        {
            Id = Guid.CreateVersion7(createdAt),
            Type = type,
            Currency = from.Currency,
            Amount = amount,
            CreatedAtUtc = createdAt,
        };

        transaction._entries.Add(LedgerEntry.Create(transaction.Id, from, -amount, createdAt));
        transaction._entries.Add(LedgerEntry.Create(transaction.Id, to, amount, createdAt));

        transaction.EnsureBalanced();
        return transaction;
    }

    private void EnsureBalanced()
    {
        if (_entries.Count < 2 || _entries.Sum(e => e.Amount) != 0m)
            throw new DomainException(DomainErrors.UnbalancedTransaction, "İşlem kayıtlarının toplamı sıfır değil.");
    }
}
