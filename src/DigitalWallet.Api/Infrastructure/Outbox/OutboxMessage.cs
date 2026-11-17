using System.Text.Json;
using DigitalWallet.Api.Domain;

namespace DigitalWallet.Api.Infrastructure.Outbox;

/// <summary>
/// Yayınlanmayı bekleyen olay. Para hareketiyle aynı DB transaction'ında yazılır (karar 6); böylece
/// "para hareket etti ama olay kayboldu" ya da "olay gitti ama para hareket etmedi" durumları oluşamaz.
/// <see cref="Id"/> RabbitMQ'da `message_id` olur; tüketici tekrarları bununla ayıklar.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Type { get; private set; } = null!;
    public string RoutingKey { get; private set; } = null!;

    /// <summary>Olayın JSON gövdesi (jsonb).</summary>
    public string Payload { get; private set; } = null!;

    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private OutboxMessage() { }

    public static OutboxMessage Create<TEvent>(TEvent @event, string type, string routingKey, DateTimeOffset occurredAtUtc)
    {
        var occurredAt = Timestamps.ToDatabasePrecision(occurredAtUtc);
        return new OutboxMessage
        {
            Id = Guid.CreateVersion7(occurredAt),
            Type = type,
            RoutingKey = routingKey,
            Payload = JsonSerializer.Serialize(@event, JsonSerializerOptions.Web),
            OccurredAtUtc = occurredAt,
        };
    }

    public void MarkPublished(DateTimeOffset nowUtc)
    {
        PublishedAtUtc = Timestamps.ToDatabasePrecision(nowUtc);
        Attempts++;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}
