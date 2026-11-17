using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.Infrastructure;
using DigitalWallet.Api.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Transfers;

/// <summary>
/// POST /transfers: müşteriden müşteriye, aynı para biriminde transfer.
/// Kayıtlar: gönderen (−tutar), alıcı (+tutar). Yetersiz bakiye / para birimi farkı → 422 (domain).
/// `Idempotency-Key` zorunlu; aynı anahtarla tekrar gelen istek parayı yeniden hareket ettirmez.
/// </summary>
public static class Transfer
{
    public const string IdempotencyScope = "transfer";

    public sealed record Request(Guid? FromWalletId, Guid? ToWalletId, decimal? Amount);

    public static IEndpointRouteBuilder MapTransfer(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transfers", HandleAsync).WithName(nameof(Transfer)).WithTags("Transfers");
        return app;
    }

    private static async Task<Results<Created<TransactionResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Request request,
        [FromHeader(Name = IdempotentExecution.HeaderName)] string? idempotencyKey,
        HttpContext httpContext,
        WalletDbContext db,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var errors = Validate(request, idempotencyKey);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var fromId = request.FromWalletId!.Value;
        var toId = request.ToWalletId!.Value;
        var amount = request.Amount!.Value;

        // Varlık kontrolü kilitten önce (cüzdanlar silinmez): WHERE id IN (@from, @to).
        var existing = await db.Wallets
            .Where(w => w.Id == fromId || w.Id == toId)
            .Select(w => w.Id)
            .ToListAsync(ct);

        if (!existing.Contains(fromId))
            return TypedResults.Problem($"Gönderen cüzdan bulunamadı: {fromId}", statusCode: StatusCodes.Status404NotFound);
        if (!existing.Contains(toId))
            return TypedResults.Problem($"Alıcı cüzdan bulunamadı: {toId}", statusCode: StatusCodes.Status404NotFound);

        var idempotencyRequest = IdempotencyRequest.Create(IdempotencyScope, idempotencyKey!,
            new { FromWalletId = fromId, ToWalletId = toId, Amount = Money.Normalize(amount) });

        var outcome = await db.ExecuteIdempotentAsync(idempotencyRequest, async token =>
        {
            // İki cüzdan id sırasıyla kilitlenir (deadlock önlemi). Bakiye kontrolü (Debit) kilitli,
            // güncel satır üzerinde çalışır.
            var wallets = await db.LockWalletsAsync([fromId, toId], token);

            var transfer = LedgerTransaction.Transfer(wallets[fromId], wallets[toId], amount, timeProvider.GetUtcNow());
            db.LedgerTransactions.Add(transfer);
            return transfer;
        }, timeProvider, ct);

        return outcome.Kind == IdempotencyOutcomeKind.KeyReusedWithDifferentRequest
            ? IdempotentResponses.KeyReused()
            : IdempotentResponses.Created(outcome, httpContext);
    }

    private static Dictionary<string, string[]> Validate(Request request, string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>();

        if (IdempotentExecution.ValidateKey(idempotencyKey) is { } keyError)
            errors[IdempotentExecution.HeaderName] = [keyError];
        if (request.FromWalletId is null)
            errors[nameof(Request.FromWalletId)] = ["Gönderen cüzdan zorunludur."];
        if (request.ToWalletId is null)
            errors[nameof(Request.ToWalletId)] = ["Alıcı cüzdan zorunludur."];
        if (request.FromWalletId is not null && request.FromWalletId == request.ToWalletId)
            errors[nameof(Request.ToWalletId)] = ["Gönderen ve alıcı cüzdan aynı olamaz."];
        if (request.Amount is not { } amount || !Money.IsValidAmount(amount))
            errors[nameof(Request.Amount)] =
                [$"Tutar 0'dan büyük, en fazla {Money.MaxAmount} ve en fazla {Money.Decimals} ondalıklı olmalı."];

        return errors;
    }
}
