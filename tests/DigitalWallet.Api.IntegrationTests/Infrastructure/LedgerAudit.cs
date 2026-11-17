using DigitalWallet.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.Api.IntegrationTests.Infrastructure;

/// <summary>
/// CLAUDE.md'deki defter denetiminin otomatik hali. Tüm veritabanı için: defter toplamı 0, dengesiz işlem yok,
/// bakiyesi defterle uyuşmayan cüzdan yok. Testler aynı (boş başlayan) veritabanını paylaştığı için
/// her test sonunda bütün testlerin verisini birlikte doğrular.
/// </summary>
public static class LedgerAudit
{
    public static async Task AssertConsistentAsync(this ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        var total = await db.Database
            .SqlQuery<decimal>($"""SELECT COALESCE(SUM(amount), 0) AS "Value" FROM ledger_entries""")
            .SingleAsync();

        var unbalanced = await db.Database
            .SqlQuery<long>($"""
                SELECT COUNT(*) AS "Value" FROM (
                    SELECT transaction_id FROM ledger_entries GROUP BY transaction_id HAVING SUM(amount) <> 0) x
                """)
            .SingleAsync();

        var mismatched = await db.Database
            .SqlQuery<long>($"""
                SELECT COUNT(*) AS "Value"
                FROM wallets w
                LEFT JOIN (SELECT wallet_id, SUM(amount) s FROM ledger_entries GROUP BY wallet_id) e ON e.wallet_id = w.id
                WHERE w.balance <> COALESCE(e.s, 0)
                """)
            .SingleAsync();

        Assert.Equal(0m, total);
        Assert.Equal(0, unbalanced);
        Assert.Equal(0, mismatched);
    }
}
