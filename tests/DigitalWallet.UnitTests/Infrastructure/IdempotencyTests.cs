using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Infrastructure.Idempotency;

namespace DigitalWallet.UnitTests.Infrastructure;

public sealed class IdempotencyTests
{
    private static readonly Guid WalletId = Guid.Parse("01a1ea8f-f593-7d8a-a836-d198ce34ec80");

    private static IdempotencyRequest Deposit(decimal amount, string scope = "deposit") =>
        IdempotencyRequest.Create(scope, "key-1", new { WalletId, Amount = Money.Normalize(amount) });

    [Fact]
    public void Same_amount_written_differently_has_same_fingerprint() =>
        Assert.Equal(Deposit(100m).Hash, Deposit(100.00m).Hash);

    [Fact]
    public void Different_amount_has_different_fingerprint() =>
        Assert.NotEqual(Deposit(100m).Hash, Deposit(50m).Hash);

    [Fact]
    public void Scope_is_part_of_fingerprint() =>
        Assert.NotEqual(Deposit(100m, "deposit").Hash, Deposit(100m, "transfer").Hash);

    [Fact]
    public void Fingerprint_is_sha256_hex() =>
        Assert.Matches("^[0-9a-f]{64}$", Deposit(1m).Hash);

    [Theory]
    [InlineData("abc-123")]
    [InlineData("01a1ea8f-f593-7d8a-a836-d198ce34ec80")]
    public void Valid_keys_are_accepted(string key) =>
        Assert.Null(IdempotentExecution.ValidateKey(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]       // boşluk
    [InlineData("anahtarı")]  // ASCII dışı
    public void Invalid_keys_are_rejected(string? key) =>
        Assert.NotNull(IdempotentExecution.ValidateKey(key));

    [Fact]
    public void Keys_longer_than_100_characters_are_rejected() =>
        Assert.NotNull(IdempotentExecution.ValidateKey(new string('k', 101)));
}
