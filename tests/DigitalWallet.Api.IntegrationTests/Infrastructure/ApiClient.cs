using System.Net;
using System.Net.Http.Json;
using DigitalWallet.Api.Features.Transactions;
using DigitalWallet.Api.Features.Wallets;

namespace DigitalWallet.Api.IntegrationTests.Infrastructure;

/// <summary>Testlerde tekrarlanan HTTP çağrıları.</summary>
public static class ApiClient
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static async Task<WalletResponse> CreateWalletAsync(this HttpClient client, string currency = "TRY", string owner = "Test")
    {
        var response = await client.PostAsJsonAsync("/wallets", new { ownerName = owner, currency });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
    }

    public static Task<HttpResponseMessage> DepositAsync(this HttpClient client, Guid walletId, decimal amount, string? key = null) =>
        client.SendAsync(Post($"/wallets/{walletId}/deposits", new { amount }, key));

    public static Task<HttpResponseMessage> TransferAsync(this HttpClient client, Guid from, Guid to, decimal amount, string? key = null) =>
        client.SendAsync(Post("/transfers", new { fromWalletId = from, toWalletId = to, amount }, key));

    /// <summary>Para yatırır ve 201 bekler (test hazırlığı için).</summary>
    public static async Task<TransactionResponse> FundAsync(this HttpClient client, Guid walletId, decimal amount)
    {
        var response = await client.DepositAsync(walletId, amount);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TransactionResponse>())!;
    }

    public static async Task<decimal> GetBalanceAsync(this HttpClient client, Guid walletId) =>
        (await client.GetFromJsonAsync<WalletResponse>($"/wallets/{walletId}"))!.Balance;

    public static async Task<string?> ErrorCodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>();
        return body is not null && body.TryGetValue("code", out var code) ? code?.ToString() : null;
    }

    public static bool IsReplay(this HttpResponseMessage response) =>
        response.Headers.TryGetValues(ReplayedHeader, out var values) && values.Contains("true");

    private static HttpRequestMessage Post(string path, object body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        // Anahtar verilmezse her istek benzersiz: idempotency dışındaki testler etkilenmesin.
        request.Headers.Add(IdempotencyHeader, key ?? Guid.NewGuid().ToString());
        return request;
    }
}
