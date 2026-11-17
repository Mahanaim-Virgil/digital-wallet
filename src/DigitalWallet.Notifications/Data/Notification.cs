namespace DigitalWallet.Notifications.Data;

public enum NotificationKind
{
    DepositReceived,
    TransferSent,
    TransferReceived,
}

/// <summary>Cüzdan sahibine gösterilecek bildirim (gerçek SMS/e-posta gönderimi kapsam dışı).</summary>
public sealed class Notification
{
    public Guid Id { get; private set; }
    public Guid WalletId { get; private set; }
    public Guid TransactionId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Message { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Notification() { }

    public Notification(Guid walletId, Guid transactionId, NotificationKind kind, string message, DateTimeOffset createdAtUtc)
    {
        Id = Guid.CreateVersion7(createdAtUtc);
        WalletId = walletId;
        TransactionId = transactionId;
        Kind = kind;
        Message = message;
        CreatedAtUtc = createdAtUtc;
    }
}

/// <summary>
/// Inbox: işlenmiş mesajların kimlikleri. RabbitMQ en az bir kez teslim eder; aynı mesaj ikinci kez gelirse
/// bu tablodaki birincil anahtar sayesinde yeniden işlenmez.
/// </summary>
public sealed class ProcessedMessage
{
    public Guid MessageId { get; private set; }
    public string Type { get; private set; } = null!;
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    private ProcessedMessage() { }

    public ProcessedMessage(Guid messageId, string type, DateTimeOffset processedAtUtc)
    {
        MessageId = messageId;
        Type = type;
        ProcessedAtUtc = processedAtUtc;
    }
}
