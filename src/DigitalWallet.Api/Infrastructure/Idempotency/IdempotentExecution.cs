using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Infrastructure.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DigitalWallet.Api.Infrastructure.Idempotency;

public enum IdempotencyOutcomeKind
{
    /// <summary>İlk kez işlendi; para hareket etti.</summary>
    Created,

    /// <summary>Aynı anahtar ve aynı istek daha önce işlenmiş; para tekrar hareket etmedi.</summary>
    Replayed,

    /// <summary>Aynı anahtar farklı bir istekle kullanılmış; reddedilmeli.</summary>
    KeyReusedWithDifferentRequest,
}

public sealed record IdempotencyOutcome(IdempotencyOutcomeKind Kind, LedgerTransaction? Transaction);

/// <summary>Anahtar + isteğin parmak izi. Parmak izi, isteğin anlamını değiştirmeyen farklara duyarsız olmalı.</summary>
public sealed record IdempotencyRequest(string Scope, string Key, string Hash)
{
    public static IdempotencyRequest Create(string scope, string key, object canonicalPayload)
    {
        var json = JsonSerializer.Serialize(canonicalPayload);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}\n{json}")));
        return new IdempotencyRequest(scope, key, hash);
    }
}

public static class IdempotentExecution
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    /// <summary>Başlık yoksa veya biçimi geçersizse hata mesajı döner.</summary>
    public static string? ValidateKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return $"{HeaderName} başlığı zorunludur (para hareketi tekrar edilmesin diye).";
        if (key.Length > IdempotencyRecord.KeyMaxLength)
            return $"{HeaderName} en fazla {IdempotencyRecord.KeyMaxLength} karakter olabilir.";
        if (key.Any(c => c is < '!' or > '~'))
            return $"{HeaderName} yalnızca boşluksuz, yazdırılabilir ASCII karakterler içerebilir.";
        return null;
    }

    /// <summary>
    /// Para hareketini en fazla bir kez yapar (karar 5). Tek DB transaction'ı:
    /// anahtar satırı INSERT → <paramref name="work"/> (kilit + domain, işlemi context'e ekler) → kayıt işleme
    /// bağlanır → commit. Aynı anahtarla eşzamanlı ikinci istek, anahtarın unique index'inde birincinin
    /// bitmesini bekler; birinci commit ederse benzersizlik hatası alır ve ilk sonucu döner.
    /// </summary>
    public static async Task<IdempotencyOutcome> ExecuteIdempotentAsync(
        this WalletDbContext db,
        IdempotencyRequest request,
        Func<CancellationToken, Task<LedgerTransaction>> work,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        var outcome = await ExecuteCoreAsync(db, request, work, timeProvider, ct);

        // İstek span'inde: para gerçekten hareket etti mi, yoksa tekrar mıydı? (karar 7)
        var span = Activity.Current;
        span?.SetTag("idempotency.outcome", outcome.Kind switch
        {
            IdempotencyOutcomeKind.Created => "created",
            IdempotencyOutcomeKind.Replayed => "replayed",
            _ => "key_reused",
        });
        if (outcome.Transaction is { } transaction)
        {
            span?.SetTag("ledger.transaction.id", transaction.Id.ToString());
            span?.SetTag("ledger.transaction.type", transaction.Type.ToString());
        }

        return outcome;
    }

    private static async Task<IdempotencyOutcome> ExecuteCoreAsync(
        WalletDbContext db,
        IdempotencyRequest request,
        Func<CancellationToken, Task<LedgerTransaction>> work,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        // Hızlı yol: daha önce tamamlanmışsa transaction açmadan sonucu dön.
        var existing = await FindAsync(db, request, ct);
        if (existing is not null)
            return await ReplayAsync(db, existing, request, ct);

        try
        {
            var transaction = await db.InTransactionAsync(async token =>
            {
                var record = new IdempotencyRecord(request.Scope, request.Key, request.Hash,
                    Timestamps.ToDatabasePrecision(timeProvider.GetUtcNow()));
                db.IdempotencyRecords.Add(record);
                // Anahtar satırı önce yazılır: eşzamanlı kopya burada bekler, para hareketine hiç başlamaz.
                await db.SaveChangesAsync(token);

                var ledgerTransaction = await work(token);
                record.Complete(ledgerTransaction.Id);
                await db.SaveChangesAsync(token);
                return ledgerTransaction;
            }, ct);

            return new IdempotencyOutcome(IdempotencyOutcomeKind.Created, transaction);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // Aynı anahtarla eşzamanlı gelen başka bir istek önce commit etti. Bizim transaction'ımız geri
            // alındı (para hareket etmedi); onun sonucunu dönüyoruz.
            db.ChangeTracker.Clear();
            var winner = await FindAsync(db, request, ct)
                ?? throw new InvalidOperationException("Benzersizlik hatası alındı ama anahtar kaydı bulunamadı.");
            return await ReplayAsync(db, winner, request, ct);
        }
    }

    private static Task<IdempotencyRecord?> FindAsync(WalletDbContext db, IdempotencyRequest request, CancellationToken ct) =>
        db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Scope == request.Scope && r.Key == request.Key, ct);

    private static async Task<IdempotencyOutcome> ReplayAsync(
        WalletDbContext db, IdempotencyRecord record, IdempotencyRequest request, CancellationToken ct)
    {
        if (record.RequestHash != request.Hash)
            return new IdempotencyOutcome(IdempotencyOutcomeKind.KeyReusedWithDifferentRequest, null);

        var transactionId = record.TransactionId
            ?? throw new InvalidOperationException("Commit edilmiş anahtar kaydında işlem kimliği boş.");

        var transaction = await db.LedgerTransactions.AsNoTracking()
            .Include(t => t.Entries)
            .SingleAsync(t => t.Id == transactionId, ct);

        return new IdempotencyOutcome(IdempotencyOutcomeKind.Replayed, transaction);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdempotencyRecordConfiguration.PrimaryKeyName,
        };
}
