using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.Infrastructure;
using DigitalWallet.Api.Infrastructure.Idempotency;
using DigitalWallet.Api.Infrastructure.Outbox;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Deposits;

/// <summary>
/// POST /wallets/{id}/deposits: dışarıdan para girişi (gerçek kart/banka entegrasyonu kapsam dışı).
/// Kayıtlar: sistem cüzdanı (−tutar), müşteri cüzdanı (+tutar).
/// `Idempotency-Key` zorunlu; aynı anahtarla tekrar gelen istek parayı yeniden hareket ettirmez.
/// </summary>
public static class Deposit
{
    public const string IdempotencyScope = "deposit";

    public sealed record Request(decimal? Amount);

    public static RouteGroupBuilder MapDeposit(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/deposits", HandleAsync).WithName(nameof(Deposit));
        return group;
    }

    private static async Task<Results<Created<TransactionResponse>, ValidationProblem, NotFound, ProblemHttpResult>> HandleAsync(
        Guid id,
        Request request,
        [FromHeader(Name = IdempotentExecution.HeaderName)] string? idempotencyKey,
        HttpContext httpContext,
        WalletDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (IdempotentExecution.ValidateKey(idempotencyKey) is { } keyError)
            errors[IdempotentExecution.HeaderName] = [keyError];
        if (request.Amount is not { } requested || !Money.IsValidAmount(requested))
            errors[nameof(Request.Amount)] =
                [$"Tutar 0'dan büyük, en fazla {Money.MaxAmount} ve en fazla {Money.Decimals} ondalıklı olmalı."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var amount = request.Amount!.Value;

        // Varlık kontrolü kilitten önce (cüzdanlar silinmez). Para birimi değişmez; sistem cüzdanını belirler.
        var currency = await db.Wallets.Where(w => w.Id == id).Select(w => w.Currency).FirstOrDefaultAsync(ct);
        if (currency is null)
            return TypedResults.NotFound();

        var systemWalletId = SystemWallets.ForCurrency(currency);

        // Parmak izi: isteğin anlamı (cüzdan + 2 haneye normalleştirilmiş tutar). 100 ile 100.00 aynı istek.
        var idempotencyRequest = IdempotencyRequest.Create(IdempotencyScope, idempotencyKey!,
            new { WalletId = id, Amount = Money.Normalize(amount) });

        var outcome = await db.ExecuteIdempotentAsync(idempotencyRequest, async token =>
        {
            // Müşteri ve sistem cüzdanı FOR UPDATE ile kilitlenir; bakiyeler güncel ve başkası değiştiremez.
            var wallets = await db.LockWalletsAsync([id, systemWalletId], token);

            var deposit = LedgerTransaction.Deposit(wallets[systemWalletId], wallets[id], amount, timeProvider.GetUtcNow());
            db.LedgerTransactions.Add(deposit);
            // Olay aynı transaction'da outbox'a: para hareketi ve olay birlikte yazılır ya da hiçbiri.
            db.OutboxMessages.Add(LedgerEvents.ToOutboxMessage(deposit));
            return deposit;
        }, timeProvider, ct);

        return outcome.Kind == IdempotencyOutcomeKind.KeyReusedWithDifferentRequest
            ? IdempotentResponses.KeyReused()
            : IdempotentResponses.Created(outcome, httpContext);
    }
}
