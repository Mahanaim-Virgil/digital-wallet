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

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wallet is null)
            return TypedResults.NotFound();

        var systemWallet = await db.Wallets.SingleAsync(
            w => w.Type == WalletType.System && w.Currency == wallet.Currency, ct);

        // Aşama 3 notu: iki cüzdan da "oku → bellekte değiştir → yaz" ile güncelleniyor; kilit yok.
        var transaction = LedgerTransaction.Deposit(systemWallet, wallet, amount, timeProvider.GetUtcNow());

        db.LedgerTransactions.Add(transaction);
        // Tek SaveChanges: işlem, iki kayıt ve iki bakiye güncellemesi tek DB transaction'ında yazılır.
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/transactions/{transaction.Id}", TransactionResponse.From(transaction));
    }
}
