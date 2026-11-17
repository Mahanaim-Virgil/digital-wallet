using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DigitalWallet.Api.Features.Wallets;

/// <summary>POST /wallets: bakiyesi sıfır olan yeni bir cüzdan açar.</summary>
public static class CreateWallet
{
    public sealed record Request(string? OwnerName, string? Currency);

    public static RouteGroupBuilder MapCreateWallet(this RouteGroupBuilder group)
    {
        group.MapPost("/", HandleAsync).WithName(nameof(CreateWallet));
        return group;
    }

    private static async Task<Results<Created<WalletResponse>, ValidationProblem>> HandleAsync(
        Request request, WalletDbContext db, TimeProvider timeProvider, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var wallet = Wallet.Open(request.OwnerName!, request.Currency!, timeProvider.GetUtcNow());

        db.Wallets.Add(wallet);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/wallets/{wallet.Id}", WalletResponse.From(wallet));
    }

    private static Dictionary<string, string[]> Validate(Request request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.OwnerName))
            errors[nameof(Request.OwnerName)] = ["Sahip adı zorunludur."];
        else if (request.OwnerName.Trim().Length > Wallet.OwnerNameMaxLength)
            errors[nameof(Request.OwnerName)] = [$"Sahip adı en fazla {Wallet.OwnerNameMaxLength} karakter olabilir."];

        if (!Currencies.IsSupported(request.Currency))
            errors[nameof(Request.Currency)] =
                [$"Para birimi şunlardan biri olmalı: {string.Join(", ", Currencies.Supported)}."];

        return errors;
    }
}
