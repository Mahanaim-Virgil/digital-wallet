namespace DigitalWallet.Api.Domain;

/// <summary>İş kuralı ihlali. API bunu 422 ProblemDetails'e çevirir (`code` alanıyla).</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class DomainErrors
{
    public const string InvalidAmount = "invalid_amount";
    public const string InsufficientFunds = "insufficient_funds";
    public const string CurrencyMismatch = "currency_mismatch";
    public const string SameWallet = "same_wallet";
    public const string SystemWalletNotAllowed = "system_wallet_not_allowed";
    public const string UnbalancedTransaction = "unbalanced_transaction";
}
