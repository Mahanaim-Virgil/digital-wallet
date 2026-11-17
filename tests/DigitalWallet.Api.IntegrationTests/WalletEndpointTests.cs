using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Wallets;
using DigitalWallet.Api.IntegrationTests.Infrastructure;

namespace DigitalWallet.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class WalletEndpointTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Created_wallet_can_be_read_back_with_zero_balance()
    {
        var created = await Client.CreateWalletAsync("usd", "Ayşe Işık");

        var read = await Client.GetFromJsonAsync<WalletResponse>($"/wallets/{created.Id}");

        Assert.NotNull(read);
        Assert.Equal(created, read); // POST ve GET aynı biçim (2 ondalık, mikro saniye)
        Assert.Equal("USD", read.Currency);
        Assert.Equal("Customer", read.Type);
        Assert.Equal(0.00m, read.Balance);
    }

    [Fact]
    public async Task Invalid_wallet_request_returns_400_with_field_errors()
    {
        var response = await Client.PostAsJsonAsync("/wallets", new { ownerName = " ", currency = "BTC" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("OwnerName", body);
        Assert.Contains("Currency", body);
    }

    [Fact]
    public async Task Malformed_json_returns_400_not_500()
    {
        var response = await Client.PostAsync("/wallets",
            new StringContent("{\"ownerName\":", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_wallet_returns_404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/wallets/{Guid.NewGuid()}")).StatusCode);
}
