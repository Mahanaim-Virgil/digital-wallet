using System.Globalization;
using DigitalWallet.Api.Domain;

namespace DigitalWallet.UnitTests.Domain;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("1")]
    [InlineData("150.75")]
    [InlineData("1000000")]
    public void IsValidAmount_accepts_positive_amounts_with_at_most_two_decimals(string value) =>
        Assert.True(Money.IsValidAmount(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.005")]      // 3 ondalık
    [InlineData("1000000.01")]  // üst sınırın üstü
    public void IsValidAmount_rejects_zero_negative_too_precise_or_too_large(string value) =>
        Assert.False(Money.IsValidAmount(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("0", "0.00")]
    [InlineData("0.0000", "0.00")]
    [InlineData("100", "100.00")]
    [InlineData("379.5000", "379.50")]
    public void Normalize_always_returns_two_decimals(string input, string expected) =>
        Assert.Equal(expected,
            Money.Normalize(decimal.Parse(input, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture));

    [Fact]
    public void EnsureValidAmount_throws_domain_exception_with_code()
    {
        var ex = Assert.Throws<DomainException>(() => Money.EnsureValidAmount(-1m));
        Assert.Equal(DomainErrors.InvalidAmount, ex.Code);
    }
}
