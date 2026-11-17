using DigitalWallet.Api.Domain;

namespace DigitalWallet.Api.Features.Wallets;

public sealed record WalletResponse(
    Guid Id,
    string OwnerName,
    string Currency,
    decimal Balance,
    DateTimeOffset CreatedAtUtc)
{
    public static WalletResponse From(Wallet wallet) =>
        new(wallet.Id, wallet.OwnerName, wallet.Currency, wallet.Balance, wallet.CreatedAtUtc);
}
