using DigitalWallet.Api.Domain;
using DigitalWallet.Contracts;

namespace DigitalWallet.Api.Infrastructure.Outbox;

/// <summary>Ledger işleminden, servisler arası olay sözleşmesine (Contracts) çeviri.</summary>
public static class LedgerEvents
{
    public static OutboxMessage ToOutboxMessage(LedgerTransaction transaction)
    {
        // Çift taraflı kayıt: çıkan taraf (−) ve giren taraf (+).
        var debit = transaction.Entries.Single(e => e.Amount < 0);
        var credit = transaction.Entries.Single(e => e.Amount > 0);

        return transaction.Type switch
        {
            TransactionType.Deposit => OutboxMessage.Create(
                new DepositCompleted(
                    transaction.Id,
                    credit.WalletId,
                    transaction.Currency,
                    Money.Normalize(transaction.Amount),
                    Money.Normalize(credit.BalanceAfter),
                    transaction.CreatedAtUtc),
                WalletEventTopology.DepositCompletedType,
                WalletEventTopology.DepositCompletedRoutingKey,
                transaction.CreatedAtUtc),

            TransactionType.Transfer => OutboxMessage.Create(
                new TransferCompleted(
                    transaction.Id,
                    debit.WalletId,
                    credit.WalletId,
                    transaction.Currency,
                    Money.Normalize(transaction.Amount),
                    Money.Normalize(debit.BalanceAfter),
                    Money.Normalize(credit.BalanceAfter),
                    transaction.CreatedAtUtc),
                WalletEventTopology.TransferCompletedType,
                WalletEventTopology.TransferCompletedRoutingKey,
                transaction.CreatedAtUtc),

            _ => throw new ArgumentOutOfRangeException(nameof(transaction), transaction.Type, "Bilinmeyen işlem türü."),
        };
    }
}
