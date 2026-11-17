using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.Features.Wallets;
using DigitalWallet.Api.IntegrationTests.Infrastructure;

namespace DigitalWallet.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class LedgerFlowTests(ApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task Deposit_then_transfer_updates_balances_and_ledger()
    {
        var ali = await Client.CreateWalletAsync();
        var ayse = await Client.CreateWalletAsync();

        var deposit = await Client.FundAsync(ali.Id, 500m);
        var transferResponse = await Client.TransferAsync(ali.Id, ayse.Id, 120.50m);

        Assert.Equal(HttpStatusCode.Created, transferResponse.StatusCode);
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransactionResponse>())!;
        Assert.Equal($"/transactions/{transfer.Id}", transferResponse.Headers.Location?.OriginalString);
        Assert.Equal(0m, deposit.Entries.Sum(e => e.Amount));
        Assert.Equal(0m, transfer.Entries.Sum(e => e.Amount));

        Assert.Equal(379.50m, await Client.GetBalanceAsync(ali.Id));
        Assert.Equal(120.50m, await Client.GetBalanceAsync(ayse.Id));

        // Ekstre: yeniden eskiye, balanceAfter ile.
        var statement = await Client.GetFromJsonAsync<GetWalletEntries.Response>($"/wallets/{ali.Id}/entries");
        Assert.Collection(statement!.Entries,
            e => { Assert.Equal("Transfer", e.Type); Assert.Equal(-120.50m, e.Amount); Assert.Equal(379.50m, e.BalanceAfter); },
            e => { Assert.Equal("Deposit", e.Type); Assert.Equal(500m, e.Amount); Assert.Equal(500m, e.BalanceAfter); });

        var read = await Client.GetFromJsonAsync<TransactionResponse>($"/transactions/{transfer.Id}");
        Assert.Equal(transfer.Id, read!.Id);

        await fixture.Factory.AssertConsistentAsync();
    }

    [Fact]
    public async Task Insufficient_funds_returns_422_with_code_and_moves_nothing()
    {
        var from = await Client.CreateWalletAsync();
        var to = await Client.CreateWalletAsync();
        await Client.FundAsync(from.Id, 10m);

        var response = await Client.TransferAsync(from.Id, to.Id, 10.01m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", await response.ErrorCodeAsync());
        Assert.Equal(10m, await Client.GetBalanceAsync(from.Id));
        Assert.Equal(0m, await Client.GetBalanceAsync(to.Id));
    }

    [Fact]
    public async Task Transfer_between_currencies_returns_422()
    {
        var tryWallet = await Client.CreateWalletAsync("TRY");
        var usdWallet = await Client.CreateWalletAsync("USD");
        await Client.FundAsync(tryWallet.Id, 10m);

        var response = await Client.TransferAsync(tryWallet.Id, usdWallet.Id, 1m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("currency_mismatch", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Deposit_to_system_wallet_returns_422()
    {
        var response = await Client.DepositAsync(Guid.Parse("00000000-0000-0000-0000-000000000949"), 5m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("system_wallet_not_allowed", await response.ErrorCodeAsync());
    }

    [Theory]
    [InlineData("10.005")]
    [InlineData("-5")]
    [InlineData("0")]
    public async Task Invalid_amount_returns_400(string amount)
    {
        var wallet = await Client.CreateWalletAsync();
        var response = await Client.DepositAsync(wallet.Id, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Money_movement_without_idempotency_key_returns_400()
    {
        var wallet = await Client.CreateWalletAsync();

        var response = await Client.PostAsJsonAsync($"/wallets/{wallet.Id}/deposits", new { amount = 5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Idempotency-Key", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Transfer_to_unknown_wallet_returns_404()
    {
        var from = await Client.CreateWalletAsync();
        await Client.FundAsync(from.Id, 5m);

        var response = await Client.TransferAsync(from.Id, Guid.NewGuid(), 1m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
