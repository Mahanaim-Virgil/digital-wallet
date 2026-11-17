using DigitalWallet.Api.Domain;

namespace DigitalWallet.Api.Features.Wallets;

public sealed record WalletResponse(
    Guid Id,
    string OwnerName,
    string Currency,
    string Type,
    decimal Balance,
    DateTimeOffset CreatedAtUtc)
{
    public static WalletResponse From(Wallet wallet) =>
        new(wallet.Id, wallet.OwnerName, wallet.Currency, wallet.Type.ToString(),
            Money.Normalize(wallet.Balance), wallet.CreatedAtUtc);
}
