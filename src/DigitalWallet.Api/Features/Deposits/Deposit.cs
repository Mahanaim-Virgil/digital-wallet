using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Deposits;

/// <summary>
/// POST /wallets/{id}/deposits: dışarıdan para girişi (gerçek kart/banka entegrasyonu kapsam dışı).
/// Kayıtlar: sistem cüzdanı (−tutar), müşteri cüzdanı (+tutar).
/// </summary>
public static class Deposit
{
    public sealed record Request(decimal? Amount);

    public static RouteGroupBuilder MapDeposit(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/deposits", HandleAsync).WithName(nameof(Deposit));
        return group;
    }

    private static async Task<Results<Created<TransactionResponse>, ValidationProblem, NotFound>> HandleAsync(
        Guid id, Request request, WalletDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        if (request.Amount is not { } amount || !Money.IsValidAmount(amount))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(Request.Amount)] = [$"Tutar 0'dan büyük, en fazla {Money.MaxAmount} ve en fazla {Money.Decimals} ondalıklı olmalı."],
            });

        // Varlık kontrolü kilitten önce (cüzdanlar silinmez). Para birimi değişmez; sistem cüzdanını belirler.
        var currency = await db.Wallets.Where(w => w.Id == id).Select(w => w.Currency).FirstOrDefaultAsync(ct);
        if (currency is null)
            return TypedResults.NotFound();

        var systemWalletId = SystemWallets.ForCurrency(currency);

        var transaction = await db.InTransactionAsync(async token =>
        {
            // Müşteri ve sistem cüzdanı FOR UPDATE ile kilitlenir; bakiyeler güncel ve başkası değiştiremez.
            var wallets = await db.LockWalletsAsync([id, systemWalletId], token);

            var deposit = LedgerTransaction.Deposit(wallets[systemWalletId], wallets[id], amount, timeProvider.GetUtcNow());
            db.LedgerTransactions.Add(deposit);
            await db.SaveChangesAsync(token);
            return deposit;
        }, ct);

        return TypedResults.Created($"/transactions/{transaction.Id}", TransactionResponse.From(transaction));
    }
}
