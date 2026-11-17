namespace DigitalWallet.Contracts;

/// <summary>
/// Servisler arası olay sözleşmeleri. Yayınlandıktan sonra alan silmek/yeniden adlandırmak tüketicileri
/// bozar; yalnızca yeni (isteğe bağlı) alan eklenir. Tutarlar 2 ondalıklı, zamanlar UTC.
/// Mesaj kimliği (RabbitMQ message_id) olay gövdesinde değil, mesaj özelliklerinde taşınır.
/// </summary>
public sealed record DepositCompleted(
    Guid TransactionId,
    Guid WalletId,
    string Currency,
    decimal Amount,
    decimal BalanceAfter,
    DateTimeOffset OccurredAtUtc);

public sealed record TransferCompleted(
    Guid TransactionId,
    Guid FromWalletId,
    Guid ToWalletId,
    string Currency,
    decimal Amount,
    decimal FromBalanceAfter,
    decimal ToBalanceAfter,
    DateTimeOffset OccurredAtUtc);

/// <summary>RabbitMQ topolojisi: iki servisin de aynı adları kullanması için tek yerde.</summary>
public static class WalletEventTopology
{
    /// <summary>Topic exchange: tüketiciler routing key ile istedikleri olaylara abone olur.</summary>
    public const string Exchange = "wallet.events";

    public const string DepositCompletedRoutingKey = "deposit.completed";
    public const string TransferCompletedRoutingKey = "transfer.completed";

    /// <summary>Mesajın `type` özelliği: tüketici gövdeyi hangi türe çözeceğini buradan bilir.</summary>
    public const string DepositCompletedType = nameof(DepositCompleted);
    public const string TransferCompletedType = nameof(TransferCompleted);
}
