using System.Net;
using DigitalWallet.Api.IntegrationTests.Infrastructure;

namespace DigitalWallet.Api.IntegrationTests;

/// <summary>
/// Karar 4 (satır kilidi). Aşama 3'te kilitsiz kodla gösterilen sorunlar: 50 × 1 TL yatırmada bakiye 4 TL
/// kalmıştı, 100 TL'den 20 × 10 TL transferin 20'si de geçmişti. Bu testler o hataların geri gelmesini önler.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Parallel_deposits_to_same_wallet_are_not_lost()
    {
        var wallet = await Client.CreateWalletAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Client.DepositAsync(wallet.Id, 1m)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(50m, await Client.GetBalanceAsync(wallet.Id));
        await fixture.Factory.AssertConsistentAsync();
    }

    [Fact]
    public async Task Parallel_transfers_cannot_overdraw_wallet()
    {
        var from = await Client.CreateWalletAsync();
        var to = await Client.CreateWalletAsync();
        await Client.FundAsync(from.Id, 100m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Client.TransferAsync(from.Id, to.Id, 10m)));

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0m, await Client.GetBalanceAsync(from.Id));
        Assert.Equal(100m, await Client.GetBalanceAsync(to.Id));
        await fixture.Factory.AssertConsistentAsync();
    }

    [Fact]
    public async Task Opposite_direction_transfers_do_not_deadlock()
    {
        var x = await Client.CreateWalletAsync();
        var y = await Client.CreateWalletAsync();
        await Client.FundAsync(x.Id, 100m);
        await Client.FundAsync(y.Id, 100m);

        // Kilitler id sırasıyla alınmasaydı X→Y ve Y→X birbirini beklerdi (40P01 deadlock).
        var responses = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            i % 2 == 0 ? Client.TransferAsync(x.Id, y.Id, 1m) : Client.TransferAsync(y.Id, x.Id, 1m)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(100m, await Client.GetBalanceAsync(x.Id));
        Assert.Equal(100m, await Client.GetBalanceAsync(y.Id));
        await fixture.Factory.AssertConsistentAsync();
    }
}
