using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.IntegrationTests.Infrastructure;

namespace DigitalWallet.Api.IntegrationTests;

/// <summary>Karar 5: aynı Idempotency-Key ile tekrar gelen istek parayı yeniden hareket ettirmez.</summary>
[Collection(ApiCollection.Name)]
public sealed class IdempotencyTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Repeated_request_returns_same_transaction_without_moving_money_again()
    {
        var wallet = await Client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();

        var first = await Client.DepositAsync(wallet.Id, 100m, key);
        var second = await Client.DepositAsync(wallet.Id, 100m, key);
        var third = await Client.DepositAsync(wallet.Id, 100.00m, key); // aynı tutarın farklı yazımı

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        Assert.False(first.IsReplay());
        Assert.True(second.IsReplay());
        Assert.True(third.IsReplay());

        var firstId = (await first.Content.ReadFromJsonAsync<TransactionResponse>())!.Id;
        Assert.Equal(firstId, (await second.Content.ReadFromJsonAsync<TransactionResponse>())!.Id);
        Assert.Equal(firstId, (await third.Content.ReadFromJsonAsync<TransactionResponse>())!.Id);
        Assert.Equal(100m, await Client.GetBalanceAsync(wallet.Id));
    }

    [Fact]
    public async Task Same_key_with_different_body_returns_422()
    {
        var wallet = await Client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();
        await Client.DepositAsync(wallet.Id, 100m, key);

        var response = await Client.DepositAsync(wallet.Id, 50m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("idempotency_key_reused", await response.ErrorCodeAsync());
        Assert.Equal(100m, await Client.GetBalanceAsync(wallet.Id));
    }

    [Fact]
    public async Task Failed_request_is_not_stored_and_can_be_retried_with_same_key()
    {
        var from = await Client.CreateWalletAsync();
        var to = await Client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();

        var rejected = await Client.TransferAsync(from.Id, to.Id, 30m, key);
        await Client.FundAsync(from.Id, 30m);
        var retried = await Client.TransferAsync(from.Id, to.Id, 30m, key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.False(retried.IsReplay());
        Assert.Equal(30m, await Client.GetBalanceAsync(to.Id));
    }

    [Fact]
    public async Task Concurrent_requests_with_same_key_move_money_exactly_once()
    {
        var wallet = await Client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();

        // Kullanıcı düğmeye art arda basıyor: aynı anahtarla aynı anda 20 istek.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Client.DepositAsync(wallet.Id, 25m, key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(responses, r => !r.IsReplay());
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<TransactionResponse>())!.Id));
        Assert.Single(ids.Distinct());
        Assert.Equal(25m, await Client.GetBalanceAsync(wallet.Id));
        await fixture.Factory.AssertConsistentAsync();
    }
}
