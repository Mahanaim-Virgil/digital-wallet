namespace DigitalWallet.Api.Domain;

/// <summary>
/// Defterdeki tek satır: bir cüzdanın bakiyesindeki işaretli değişim (+ giriş, − çıkış).
/// Oluşturulduktan sonra değişmez.
/// </summary>
public sealed class LedgerEntry
{
    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid WalletId { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Bu kayıttan sonraki cüzdan bakiyesi (ekstre ve satır bazında denetim için).</summary>
    public decimal BalanceAfter { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private LedgerEntry() { }

    internal static LedgerEntry Create(Guid transactionId, Wallet wallet, decimal signedAmount, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.CreateVersion7(createdAt),
            TransactionId = transactionId,
            WalletId = wallet.Id,
            Amount = signedAmount,
            BalanceAfter = wallet.Balance,
            CreatedAtUtc = createdAt,
        };
}
