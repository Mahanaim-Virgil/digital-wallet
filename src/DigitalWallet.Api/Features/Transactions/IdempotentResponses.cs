using DigitalWallet.Api.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DigitalWallet.Api.Features.Transactions;

/// <summary>Para hareketi uç noktalarının ortak idempotency yanıtları.</summary>
public static class IdempotentResponses
{
    /// <summary>İlk işlemde ve tekrar gelen istekte aynı 201; tekrarda `Idempotent-Replayed: true` başlığı.</summary>
    public static Created<TransactionResponse> Created(IdempotencyOutcome outcome, HttpContext httpContext)
    {
        var transaction = outcome.Transaction
            ?? throw new InvalidOperationException("Başarılı idempotency sonucunda işlem boş olamaz.");

        if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
            httpContext.Response.Headers[IdempotentExecution.ReplayedHeaderName] = "true";

        return TypedResults.Created($"/transactions/{transaction.Id}", TransactionResponse.From(transaction));
    }

    public static ProblemHttpResult KeyReused() =>
        TypedResults.Problem(
            title: "İş kuralı ihlali",
            detail: $"Bu {IdempotentExecution.HeaderName} daha önce farklı bir istekle kullanıldı.",
            statusCode: StatusCodes.Status422UnprocessableEntity,
            extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_reused" });
}
