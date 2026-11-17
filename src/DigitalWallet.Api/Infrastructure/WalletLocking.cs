using DigitalWallet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Infrastructure;

/// <summary>Para hareketleri için açık transaction ve satır kilidi (bkz. docs/MIMARI-KARARLAR.md, karar 4).</summary>
public static class WalletLocking
{
    /// <summary>
    /// İşi açık bir DB transaction'ında çalıştırır. Aspire'ın Npgsql bileşeni geçici hatalarda yeniden deneme
    /// stratejisini açar; bu strateji açıkken elle başlatılan transaction'lar strateji içine sarılmalıdır.
    /// İş istisna atarsa transaction commit edilmeden kapanır (geri alınır).
    /// </summary>
    public static Task<T> InTransactionAsync<T>(
        this WalletDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return strategy.ExecuteAsync(async () =>
        {
            // Yeniden denemede önceki başarısız denemenin izlediği nesneler karışmasın.
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await work(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }

    /// <summary>
    /// Cüzdan satırlarını kilitler ve güncel hallerini döner. Kilit transaction bitene kadar sürer;
    /// aynı satırı isteyen diğer transaction'lar bekler.
    /// Kilitler her zaman id sırasıyla alınır: A→B ve B→A transferleri birbirini sonsuza kadar beklemez.
    /// </summary>
    public static async Task<Dictionary<Guid, Wallet>> LockWalletsAsync(
        this WalletDbContext db, IEnumerable<Guid> walletIds, CancellationToken ct)
    {
        var ids = walletIds.Distinct().ToArray();

        // Span süresi = kilit bekleme + sorgu süresi; paralel yükte darboğaz burada görünür (karar 7).
        using var activity = ApiTelemetry.Source.StartActivity("wallet.lock");
        activity?.SetTag("wallet.lock.count", ids.Length);

        // FromSql parametreleri SQL parametresine çevirir (SQL injection yok).
        var wallets = await db.Wallets
            .FromSql($"SELECT * FROM wallets WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToListAsync(ct);

        if (wallets.Count != ids.Length)
            throw new InvalidOperationException("Kilitlenecek cüzdanlardan biri bulunamadı; varlık kontrolü transaction'dan önce yapılmalı.");

        return wallets.ToDictionary(w => w.Id);
    }
}
