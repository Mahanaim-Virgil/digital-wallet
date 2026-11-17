using System.Text;
using DigitalWallet.Contracts;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace DigitalWallet.Api.Infrastructure.Outbox;

/// <summary>
/// Outbox'taki bekleyen olayları RabbitMQ'ya taşır (karar 6). Teslim garantisi: en az bir kez.
/// Gönderip işaretlemeden önce çökerse mesaj tekrar gider; tüketici `message_id` ile ayıklar.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = IdleDelay;
            try
            {
                var (published, failed) = await PublishBatchAsync(stoppingToken);
                if (published > 0)
                    logger.LogInformation("Outbox: {Count} mesaj yayınlandı.", published);

                if (failed)
                    delay = ErrorDelay;
                else if (published == BatchSize)
                    continue; // Parti doluydu; muhtemelen daha fazla bekleyen var, beklemeden devam.
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // RabbitMQ'ya bağlanılamıyor ya da DB geçici olarak yok: mesajlar outbox'ta güvende, sonra tekrar.
                logger.LogWarning(ex, "Outbox yayıncısı hata aldı; {Delay} sn sonra yeniden denenecek.", ErrorDelay.TotalSeconds);
                await CloseChannelAsync();
                delay = ErrorDelay;
            }

            try { await Task.Delay(delay, timeProvider, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        await CloseChannelAsync();
    }

    private async Task<(int Published, bool Failed)> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        // Ucuz, kilitsiz kontrol: bekleyen yoksa RabbitMQ'ya hiç dokunma.
        if (!await db.OutboxMessages.AnyAsync(m => m.PublishedAtUtc == null, ct))
            return (0, false);

        // Kanal DB transaction'ından ÖNCE alınır: RabbitMQ kapalıysa bağlantı denemeleri sürerken
        // outbox satırları kilitli kalmasın.
        var channel = await GetChannelAsync(ct);

        return await db.InTransactionAsync(async token =>
        {
            // SKIP LOCKED: başka bir API kopyası aynı anda yayınlıyorsa onun kilitlediği satırları atlarız;
            // aynı mesajı iki kopya birden göndermez.
            var messages = await db.OutboxMessages
                .FromSql($"""
                    SELECT * FROM outbox_messages
                    WHERE published_at_utc IS NULL
                    ORDER BY id
                    LIMIT {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(token);

            if (messages.Count == 0)
                return (0, false);

            var published = 0;
            var failed = false;

            foreach (var message in messages)
            {
                try
                {
                    var properties = new BasicProperties
                    {
                        MessageId = message.Id.ToString(),
                        Type = message.Type,
                        ContentType = "application/json",
                        DeliveryMode = DeliveryModes.Persistent, // RabbitMQ yeniden başlasa da kuyrukta kalır
                        Timestamp = new AmqpTimestamp(message.OccurredAtUtc.ToUnixTimeSeconds()),
                    };

                    // mandatory: hiçbir kuyruğa yönlenemezse RabbitMQ mesajı geri çevirir (PublishReturnException);
                    // sessizce kaybolmaz, outbox'ta kalır. Publisher confirm: RabbitMQ "aldım" diyene kadar bekler.
                    await channel.BasicPublishAsync(
                        WalletEventTopology.Exchange, message.RoutingKey, mandatory: true,
                        properties, Encoding.UTF8.GetBytes(message.Payload), token);

                    message.MarkPublished(timeProvider.GetUtcNow());
                    published++;
                }
                catch (Exception ex) when (ex is PublishException or AlreadyClosedException or OperationInterruptedException)
                {
                    message.RecordFailure(ex.Message);
                    logger.LogWarning(ex, "Outbox mesajı {MessageId} ({Type}) gönderilemedi; deneme {Attempts}.",
                        message.Id, message.Type, message.Attempts);
                    failed = true;
                    break; // Sırayı korumak için partiyi burada kes; kalanlar sonraki turda.
                }
            }

            await db.SaveChangesAsync(token);

            if (failed)
                await CloseChannelAsync(); // Kanal bozulmuş olabilir; sonraki turda yenisi açılır.

            return (published, failed);
        }, ct);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        // IConnection'ı her seferinde DI'dan istiyoruz: RabbitMQ açılışta kapalıysa çözümleme hata verir,
        // sonraki denemede tekrar bağlanmayı dener.
        var connection = serviceProvider.GetRequiredService<IConnection>();

        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);

        // Exchange'i yayıncı da tanımlar (idempotent işlem); kuyrukları tüketiciler tanımlar.
        await _channel.ExchangeDeclareAsync(WalletEventTopology.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        return _channel;
    }

    private async Task CloseChannelAsync()
    {
        if (_channel is null)
            return;

        try { await _channel.DisposeAsync(); }
        catch (Exception ex) { logger.LogDebug(ex, "Kanal kapatılırken hata (yok sayıldı)."); }
        _channel = null;
    }
}
