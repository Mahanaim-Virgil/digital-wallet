using System.Net;
using System.Text.Json;
using DigitalWallet.Api.Infrastructure;
using DigitalWallet.Api.Infrastructure.Outbox;
using DigitalWallet.Api.IntegrationTests.Infrastructure;
using DigitalWallet.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace DigitalWallet.Api.IntegrationTests;

/// <summary>Karar 6 (transactional outbox) ve karar 7 (olayın trace bağlamı).</summary>
[Collection(ApiCollection.Name)]
public sealed class OutboxTests(ApiFixture fixture)
{
    private static async Task<OutboxMessage?> FindOutboxMessageAsync(ApiFactory factory, Guid transactionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        return await db.OutboxMessages
            .FromSql($"SELECT * FROM outbox_messages WHERE payload->>'transactionId' = {transactionId.ToString()}")
            .AsNoTracking()
            .SingleOrDefaultAsync();
    }

    [Fact]
    public async Task Money_movement_writes_exactly_one_event_with_trace_context_in_same_transaction()
    {
        var wallet = await fixture.Client.CreateWalletAsync();

        var deposit = await fixture.Client.FundAsync(wallet.Id, 42m);

        var message = await FindOutboxMessageAsync(fixture.Factory, deposit.Id);
        Assert.NotNull(message);
        Assert.Equal(WalletEventTopology.DepositCompletedType, message.Type);
        Assert.Equal(WalletEventTopology.DepositCompletedRoutingKey, message.RoutingKey);
        Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", message.TraceParent ?? "");
        Assert.Null(message.PublishedAtUtc); // Bu API örneğinde yayıncı kapalı.
    }

    [Fact]
    public async Task Api_accepts_money_movement_while_rabbitmq_is_unreachable_and_keeps_event_pending()
    {
        // Yayıncı açık ama RabbitMQ adresi bilerek ulaşılamaz.
        await using var factory = fixture.CreateFactory(publisherEnabled: true, rabbitConnectionString: "amqp://guest:guest@127.0.0.1:1/");
        using var client = factory.CreateClient();
        var wallet = await client.CreateWalletAsync();

        var response = await client.DepositAsync(wallet.Id, 10m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10m, await client.GetBalanceAsync(wallet.Id));

        var transactionId = (await System.Net.Http.Json.HttpContentJsonExtensions
            .ReadFromJsonAsync<DigitalWallet.Api.Features.Transactions.TransactionResponse>(response.Content))!.Id;
        await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken); // yayıncıya birkaç tur fırsat ver
        var message = await FindOutboxMessageAsync(factory, transactionId);
        Assert.NotNull(message);
        Assert.Null(message.PublishedAtUtc); // Kaybolmadı, gönderilmeyi bekliyor.
    }

    [Fact]
    public async Task Pending_event_reaches_rabbitmq_with_outbox_id_as_message_id()
    {
        var ct = TestContext.Current.CancellationToken;

        // Testin kendi geçici kuyruğu: wallet.events'teki her olayı alır.
        var connectionFactory = new ConnectionFactory { Uri = new Uri(fixture.RabbitConnectionString) };
        await using var connection = await connectionFactory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await channel.ExchangeDeclareAsync(WalletEventTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        var queue = (await channel.QueueDeclareAsync("", durable: false, exclusive: true, autoDelete: true, cancellationToken: ct)).QueueName;
        await channel.QueueBindAsync(queue, WalletEventTopology.Exchange, "#", cancellationToken: ct);

        await using var factory = fixture.CreateFactory(publisherEnabled: true);
        using var client = factory.CreateClient();
        var wallet = await client.CreateWalletAsync();
        var deposit = await client.FundAsync(wallet.Id, 42m);
        var outboxId = (await FindOutboxMessageAsync(factory, deposit.Id))!.Id.ToString();

        // Yayıncı ≈1 sn'de bir çalışır; önceki testlerin bekleyen olayları da gelebilir, bizimkini ara.
        BasicGetResult? received = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (received is null && DateTime.UtcNow < deadline)
        {
            var next = await channel.BasicGetAsync(queue, autoAck: true, ct);
            if (next is null)
                await Task.Delay(200, ct);
            else if (next.BasicProperties.MessageId == outboxId)
                received = next;
        }

        Assert.NotNull(received);
        Assert.Equal(WalletEventTopology.DepositCompletedType, received.BasicProperties.Type);
        Assert.Equal(DeliveryModes.Persistent, received.BasicProperties.DeliveryMode);
        var payload = JsonDocument.Parse(received.Body).RootElement;
        Assert.Equal(deposit.Id, payload.GetProperty("transactionId").GetGuid());
        Assert.Equal(42.00m, payload.GetProperty("amount").GetDecimal());

        var published = await FindOutboxMessageAsync(factory, deposit.Id);
        Assert.NotNull(published!.PublishedAtUtc);
    }
}
