using DigitalWallet.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Transactions;

/// <summary>GET /transactions/{id}: işlem ve kayıtları (para yatırma/transfer yanıtlarındaki Location).</summary>
public static class GetTransaction
{
    public static IEndpointRouteBuilder MapGetTransaction(this IEndpointRouteBuilder app)
    {
        app.MapGet("/transactions/{id:guid}", HandleAsync).WithName(nameof(GetTransaction)).WithTags("Transactions");
        return app;
    }

    private static async Task<Results<Ok<TransactionResponse>, NotFound>> HandleAsync(
        Guid id, WalletDbContext db, CancellationToken ct)
    {
        var transaction = await db.LedgerTransactions
            .AsNoTracking()
            .Include(t => t.Entries)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        return transaction is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(TransactionResponse.From(transaction));
    }
}
