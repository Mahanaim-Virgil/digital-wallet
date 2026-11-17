using DigitalWallet.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DigitalWallet.Notifications.Messaging;

/// <summary>
/// `wallet.events` exchange'inden para hareketi olaylarını dinler (karar 6).
/// Manuel ack: mesaj ancak işlendikten (DB'ye yazıldıktan) sonra onaylanır; servis arada çökerse
/// RabbitMQ mesajı yeniden teslim eder, inbox tekrarı ayıklar.
/// </summary>
public sealed class WalletEventsConsumer(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    ILogger<WalletEventsConsumer> logger) : BackgroundService
{
    public const string Queue = "notifications.wallet-events";
    public const string DeadLetterExchange = "notifications.wallet-events.dlx";
    public const string DeadLetterQueue = "notifications.wallet-events.dlq";
    private const ushort PrefetchCount = 10;

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Açılışta RabbitMQ'ya ulaşılamazsa bekleyip yeniden dene. Bağlantı sonradan koparsa RabbitMQ.Client'ın
        // otomatik kurtarması (automatic recovery) bağlantıyı, kanalı ve tüketiciyi yeniden kurar.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                logger.LogInformation("'{Queue}' kuyruğu dinleniyor.", Queue);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "RabbitMQ tüketicisi başlatılamadı; 5 sn sonra yeniden denenecek.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
    }

    private async Task StartConsumingAsync(CancellationToken ct)
    {
        var connection = serviceProvider.GetRequiredService<IConnection>();
        var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        // Topoloji (hepsi idempotent: tekrar tanımlamak zararsız).
        await channel.ExchangeDeclareAsync(WalletEventTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: "", cancellationToken: ct);

        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: ct);
        await channel.QueueBindAsync(Queue, WalletEventTopology.Exchange, WalletEventTopology.DepositCompletedRoutingKey, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, WalletEventTopology.Exchange, WalletEventTopology.TransferCompletedRoutingKey, cancellationToken: ct);

        // Aynı anda en fazla 10 onaylanmamış mesaj: tüketici yetişemezse mesajlar kuyrukta bekler.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, ct);
        await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, ct);

        _channel = channel;
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var messageIdText = delivery.BasicProperties.MessageId;
        var type = delivery.BasicProperties.Type;

        try
        {
            if (!Guid.TryParse(messageIdText, out var messageId))
                throw new PoisonMessageException($"Geçersiz veya eksik message_id: '{messageIdText}'.");

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<WalletEventProcessor>();
            await processor.ProcessAsync(messageId, type, delivery.Body, ct);

            // DB'ye yazıldıktan SONRA onay. Buradan önce çökersek mesaj yeniden gelir, inbox ayıklar.
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
        }
        catch (PoisonMessageException ex)
        {
            // Yeniden denemek düzeltmez: doğrudan dead-letter kuyruğuna.
            logger.LogError(ex, "Bozuk mesaj {MessageId} ({Type}) dead-letter kuyruğuna gönderildi.", messageIdText, type);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Geçici olabilir (ör. DB kısa süre yok): bir kez daha dene; ikinci kez de olursa dead-letter.
            var requeue = !delivery.Redelivered;
            logger.LogWarning(ex, "Mesaj {MessageId} işlenemedi; {Action}.", messageIdText,
                requeue ? "kuyruğa geri konuyor" : "ikinci hata, dead-letter kuyruğuna gönderiliyor");
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue, ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            try { await _channel.CloseAsync(cancellationToken); }
            catch (Exception ex) { logger.LogDebug(ex, "Kanal kapatılırken hata (yok sayıldı)."); }
        }
        await base.StopAsync(cancellationToken);
    }
}
