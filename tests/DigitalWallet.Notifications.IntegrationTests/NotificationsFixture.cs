using System.Text;
using System.Text.Json;
using DigitalWallet.Contracts;
using DigitalWallet.Notifications.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace DigitalWallet.Notifications.IntegrationTests;

/// <summary>
/// Gerçek PostgreSQL + RabbitMQ; bildirim servisi bellekte (tüketici arka planda çalışır).
/// Testler mesajları doğrudan `wallet.events` exchange'ine yayınlar (API'nin outbox yayıncısı gibi).
/// </summary>
public sealed class NotificationsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3-management").Build();

    private IConnection _connection = null!;
    private IChannel _channel = null!;

    public NotificationsFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public IChannel Channel => _channel;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        _connection = await new ConnectionFactory { Uri = new Uri(_rabbit.GetConnectionString()) }.CreateConnectionAsync();
        _channel = await _connection.CreateChannelAsync();
        await DeclareTopologyAsync(_channel);

        Factory = new NotificationsFactory(_postgres.GetConnectionString(), _rabbit.GetConnectionString());
        Client = Factory.CreateClient();
    }

    /// <summary>
    /// Tüketicinin tanımladığı topolojinin aynısı (aynı ayarlarla yeniden tanımlamak zararsız). Böylece test,
    /// tüketici arka planda kuyruğu kurmadan önce yayın yaparsa mesaj kaybolmaz.
    /// </summary>
    private static async Task DeclareTopologyAsync(IChannel channel)
    {
        await channel.ExchangeDeclareAsync(WalletEventTopology.Exchange, ExchangeType.Topic, durable: true);
        await channel.ExchangeDeclareAsync(WalletEventsConsumer.DeadLetterExchange, ExchangeType.Fanout, durable: true);
        await channel.QueueDeclareAsync(WalletEventsConsumer.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(WalletEventsConsumer.DeadLetterQueue, WalletEventsConsumer.DeadLetterExchange, "");
        await channel.QueueDeclareAsync(WalletEventsConsumer.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = WalletEventsConsumer.DeadLetterExchange });
        await channel.QueueBindAsync(WalletEventsConsumer.Queue, WalletEventTopology.Exchange, WalletEventTopology.DepositCompletedRoutingKey);
        await channel.QueueBindAsync(WalletEventsConsumer.Queue, WalletEventTopology.Exchange, WalletEventTopology.TransferCompletedRoutingKey);
    }

    /// <summary>Olayı API'nin yayıncısıyla aynı biçimde gönderir (message_id, type, JSON gövde).</summary>
    public Task PublishAsync(Guid messageId, string type, string routingKey, object @event) =>
        PublishRawAsync(messageId.ToString(), type, routingKey, JsonSerializer.Serialize(@event, JsonSerializerOptions.Web));

    public async Task PublishRawAsync(string messageId, string type, string routingKey, string body)
    {
        var properties = new BasicProperties
        {
            MessageId = messageId,
            Type = type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
        };
        await _channel.BasicPublishAsync(WalletEventTopology.Exchange, routingKey, mandatory: false,
            properties, Encoding.UTF8.GetBytes(body));
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
            await Factory.DisposeAsync();
        if (_channel is not null)
            await _channel.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class NotificationsCollection : ICollectionFixture<NotificationsFixture>
{
    public const string Name = "notifications";
}

public sealed class NotificationsFactory(string postgres, string rabbit) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:notificationsdb", postgres);
        builder.UseSetting("ConnectionStrings:messaging", rabbit);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
    }
}
