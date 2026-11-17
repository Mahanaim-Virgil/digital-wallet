using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Transfers;

/// <summary>
/// POST /transfers: müşteriden müşteriye, aynı para biriminde transfer.
/// Kayıtlar: gönderen (−tutar), alıcı (+tutar). Yetersiz bakiye / para birimi farkı → 422 (domain).
/// </summary>
public static class Transfer
{
    public sealed record Request(Guid? FromWalletId, Guid? ToWalletId, decimal? Amount);

    public static IEndpointRouteBuilder MapTransfer(this IEndpointRouteBuilder app)
    {
        app.MapPost("/transfers", HandleAsync).WithName(nameof(Transfer)).WithTags("Transfers");
        return app;
    }

    private static async Task<Results<Created<TransactionResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        Request request, WalletDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var fromId = request.FromWalletId!.Value;
        var toId = request.ToWalletId!.Value;

        // İki cüzdan tek sorguda: WHERE id IN (@from, @to).
        var wallets = await db.Wallets
            .Where(w => w.Id == fromId || w.Id == toId)
            .ToDictionaryAsync(w => w.Id, ct);

        if (!wallets.TryGetValue(fromId, out var from))
            return TypedResults.Problem($"Gönderen cüzdan bulunamadı: {fromId}", statusCode: StatusCodes.Status404NotFound);
        if (!wallets.TryGetValue(toId, out var to))
            return TypedResults.Problem($"Alıcı cüzdan bulunamadı: {toId}", statusCode: StatusCodes.Status404NotFound);

        // Aşama 3 notu: bakiye kontrolü (Debit) bellekteki değere bakar; paralel transferlerde
        // iki istek aynı eski bakiyeyi görebilir. Kilit/versiyon Aşama 3'te.
        var transaction = LedgerTransaction.Transfer(from, to, request.Amount!.Value, timeProvider.GetUtcNow());

        db.LedgerTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/transactions/{transaction.Id}", TransactionResponse.From(transaction));
    }

    private static Dictionary<string, string[]> Validate(Request request)
    {
        var errors = new Dictionary<string, string[]>();

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
