using DigitalWallet.Api.Domain;

namespace DigitalWallet.Api.Features.Transactions;

public sealed record TransactionResponse(
    Guid Id,
    string Type,
    string Currency,
    decimal Amount,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<TransactionResponse.Entry> Entries)
{
    public sealed record Entry(Guid WalletId, decimal Amount, decimal BalanceAfter);

    public static TransactionResponse From(LedgerTransaction transaction) =>
        new(transaction.Id,
            transaction.Type.ToString(),
            transaction.Currency,
            Money.Normalize(transaction.Amount),
            transaction.CreatedAtUtc,
            transaction.Entries
                .Select(e => new Entry(e.WalletId, Money.Normalize(e.Amount), Money.Normalize(e.BalanceAfter)))
                .ToList());
}
