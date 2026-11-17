using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Wallets;

/// <summary>GET /wallets/{id}/entries?limit=50: hesap ekstresi, yeniden eskiye.</summary>
public static class GetWalletEntries
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public sealed record Response(Guid WalletId, string Currency, decimal Balance, IReadOnlyList<EntryItem> Entries);

    public sealed record EntryItem(
        Guid EntryId, Guid TransactionId, string Type, decimal Amount, decimal BalanceAfter, DateTimeOffset CreatedAtUtc);

    public static RouteGroupBuilder MapGetWalletEntries(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/entries", HandleAsync).WithName(nameof(GetWalletEntries));
        return group;
    }

    private static async Task<Results<Ok<Response>, NotFound, ValidationProblem>> HandleAsync(
        Guid id, int? limit, WalletDbContext db, CancellationToken ct)
    {
        var take = limit ?? DefaultLimit;
        if (take is < 1 or > MaxLimit)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["limit"] = [$"limit 1 ile {MaxLimit} arasında olmalı."],
            });

        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wallet is null)
            return TypedResults.NotFound();

        // Kayıtta işlem türü yok; işlem tablosuyla birleştiriyoruz (tek SQL sorgusu, JOIN).
        var rows = await (
                from entry in db.LedgerEntries.AsNoTracking()
                join transaction in db.LedgerTransactions on entry.TransactionId equals transaction.Id
                where entry.WalletId == id
                orderby entry.CreatedAtUtc descending, entry.Id descending
                select new { entry.Id, entry.TransactionId, transaction.Type, entry.Amount, entry.BalanceAfter, entry.CreatedAtUtc })
            .Take(take)
            .ToListAsync(ct);

        var entries = rows
            .Select(r => new EntryItem(r.Id, r.TransactionId, r.Type.ToString(),
                Money.Normalize(r.Amount), Money.Normalize(r.BalanceAfter), r.CreatedAtUtc))
            .ToList();

        return TypedResults.Ok(new Response(wallet.Id, wallet.Currency, Money.Normalize(wallet.Balance), entries));
    }
}
