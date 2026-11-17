namespace DigitalWallet.Api.Infrastructure.Idempotency;

/// <summary>
/// Bir Idempotency-Key'in kaydı. Para hareketiyle aynı DB transaction'ında yazılır (karar 5).
/// Yanıt gövdesi saklanmaz; tekrar gelen istekte yanıt <see cref="TransactionId"/>'deki değişmeyen
/// ledger işleminden yeniden üretilir.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int KeyMaxLength = 100;
    public const int ScopeMaxLength = 50;

    /// <summary>Anahtarın geçerli olduğu uç nokta (ör. "deposit", "transfer").</summary>
    public string Scope { get; private set; } = null!;

    public string Key { get; private set; } = null!;

    /// <summary>İsteğin SHA-256 özeti (hex). Aynı anahtar farklı istekle gelirse reddetmek için.</summary>
    public string RequestHash { get; private set; } = null!;

    /// <summary>Oluşan ledger işlemi. Transaction içinde kısa süre null; commit edilen kayıtta hep dolu.</summary>
    public Guid? TransactionId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    private IdempotencyRecord() { }

    public IdempotencyRecord(string scope, string key, string requestHash, DateTimeOffset createdAtUtc)
    {
        Scope = scope;
        Key = key;
        RequestHash = requestHash;
        CreatedAtUtc = createdAtUtc;
    }

    public void Complete(Guid transactionId) => TransactionId = transactionId;
}
