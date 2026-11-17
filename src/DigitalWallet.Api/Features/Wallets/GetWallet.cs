using DigitalWallet.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Features.Wallets;

/// <summary>GET /wallets/{id}: cüzdanı ve güncel bakiyesini döner.</summary>
public static class GetWallet
{
    public static RouteGroupBuilder MapGetWallet(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", HandleAsync).WithName(nameof(GetWallet));
        return group;
    }

    private static async Task<Results<Ok<WalletResponse>, NotFound>> HandleAsync(
        Guid id, WalletDbContext db, CancellationToken ct)
    {
        // Salt okuma: değişiklik takibine gerek yok.
        var wallet = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

        return wallet is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(WalletResponse.From(wallet));
    }
}
