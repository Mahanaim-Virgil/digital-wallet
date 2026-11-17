using DigitalWallet.Api.Domain;

namespace DigitalWallet.UnitTests.Domain;

public sealed class WalletTests
{
    // 123 ms + 456 µs + 7 tick (700 ns): mikro saniyenin altında bir parça olsun ki kırpma test edilsin.
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 18, 10, 0, 0, 123, 456, TimeSpan.Zero).AddTicks(7);

    [Fact]
    public void Open_creates_empty_customer_wallet_with_normalized_fields()
    {
        var wallet = Wallet.Open("  Ayşe  ", "try", Now);

        Assert.Equal("Ayşe", wallet.OwnerName);
        Assert.Equal("TRY", wallet.Currency);
        Assert.Equal(WalletType.Customer, wallet.Type);
        Assert.Equal(0m, wallet.Balance);
        Assert.Equal(7, wallet.Id.Version); // Guid v7: zaman sıralı
    }

    [Theory]
    [InlineData("", "TRY")]
    [InlineData("   ", "TRY")]
    [InlineData("Ali", "BTC")]
    public void Open_rejects_invalid_input(string owner, string currency) =>
        Assert.Throws<ArgumentException>(() => Wallet.Open(owner, currency, Now));

    [Fact]
    public void Created_at_is_truncated_to_microseconds_like_postgres()
    {
        var wallet = Wallet.Open("Ali", "TRY", Now);
        Assert.Equal(0, wallet.CreatedAtUtc.Ticks % 10);
        Assert.Equal(TimeSpan.Zero, wallet.CreatedAtUtc.Offset);
    }

    [Fact]
    public void Customer_cannot_debit_more_than_balance()
    {
        var wallet = Wallet.Open("Ali", "TRY", Now);
        var ex = Assert.Throws<DomainException>(() => wallet.Debit(1m));
        Assert.Equal(DomainErrors.InsufficientFunds, ex.Code);
    }
}
