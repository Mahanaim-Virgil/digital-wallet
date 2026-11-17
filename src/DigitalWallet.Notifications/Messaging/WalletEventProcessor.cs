using System.Globalization;
using System.Text.Json;
using DigitalWallet.Contracts;
using DigitalWallet.Notifications.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalWallet.Notifications.Messaging;

public enum ProcessResult
{
    Processed,
    Duplicate,
}

/// <summary>Mesaj gövdesi çözülemiyor veya türü bilinmiyor: yeniden denemek işe yaramaz (dead-letter).</summary>
public sealed class PoisonMessageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Tek bir olayı işler. Inbox satırı ve bildirimler tek SaveChanges'ta (tek DB transaction'ı) yazılır:
/// ya ikisi birden kaydedilir ya hiçbiri. Aynı mesaj ikinci kez gelirse <see cref="ProcessResult.Duplicate"/>.
/// </summary>
public sealed class WalletEventProcessor(NotificationsDbContext db, TimeProvider timeProvider, ILogger<WalletEventProcessor> logger)
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<ProcessResult> ProcessAsync(Guid messageId, string? type, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        // Hızlı yol: daha önce işlendiyse gövdeyi çözmeye bile gerek yok.
        if (await db.ProcessedMessages.AnyAsync(p => p.MessageId == messageId, ct))
        {
            logger.LogInformation("Mesaj {MessageId} daha önce işlenmiş; atlandı (tekrar teslim).", messageId);
            return ProcessResult.Duplicate;
        }

        var now = timeProvider.GetUtcNow();
        var notifications = type switch
        {
            WalletEventTopology.DepositCompletedType => FromDeposit(Deserialize<DepositCompleted>(body), now),
            WalletEventTopology.TransferCompletedType => FromTransfer(Deserialize<TransferCompleted>(body), now),
            _ => throw new PoisonMessageException($"Bilinmeyen mesaj türü: '{type}'."),
        };

        db.ProcessedMessages.Add(new ProcessedMessage(messageId, type, now));
        db.Notifications.AddRange(notifications);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NotificationsDbContext.ProcessedMessagesPrimaryKey,
        })
        {
            // Aynı mesaj eşzamanlı olarak başka bir tüketicide işlendi (yarış). Bizim yazdıklarımız geri alındı.
            db.ChangeTracker.Clear();
            logger.LogInformation("Mesaj {MessageId} eşzamanlı olarak işlenmiş; atlandı.", messageId);
            return ProcessResult.Duplicate;
        }

        logger.LogInformation("Mesaj {MessageId} ({Type}) işlendi: {Count} bildirim.", messageId, type, notifications.Count);
        return ProcessResult.Processed;
    }

    private static T Deserialize<T>(ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body.Span, JsonSerializerOptions.Web)
                ?? throw new PoisonMessageException($"{typeof(T).Name} gövdesi boş.");
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException($"{typeof(T).Name} gövdesi çözülemedi.", ex);
        }
    }

    private static List<Notification> FromDeposit(DepositCompleted e, DateTimeOffset now) =>
    [
        new(e.WalletId, e.TransactionId, NotificationKind.DepositReceived,
            $"Cüzdanınıza {Format(e.Amount, e.Currency)} yatırıldı. Yeni bakiye: {Format(e.BalanceAfter, e.Currency)}.", now),
    ];

    private static List<Notification> FromTransfer(TransferCompleted e, DateTimeOffset now) =>
    [
        new(e.FromWalletId, e.TransactionId, NotificationKind.TransferSent,
            $"Cüzdanınızdan {Format(e.Amount, e.Currency)} gönderildi. Yeni bakiye: {Format(e.FromBalanceAfter, e.Currency)}.", now),
        new(e.ToWalletId, e.TransactionId, NotificationKind.TransferReceived,
            $"Cüzdanınıza {Format(e.Amount, e.Currency)} geldi. Yeni bakiye: {Format(e.ToBalanceAfter, e.Currency)}.", now),
    ];

    private static string Format(decimal amount, string currency) => $"{amount.ToString("N2", Turkish)} {currency}";
}
