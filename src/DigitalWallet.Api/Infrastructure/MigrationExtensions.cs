using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Infrastructure;

public static class MigrationExtensions
{
    /// <summary>
    /// `Database:ApplyMigrationsOnStartup` true ise bekleyen migration'ları uygular.
    /// Varsayılan false; yalnızca yerel geliştirmede AppHost açar (bkz. docs/MIMARI-KARARLAR.md, karar 2).
    /// </summary>
    public static async Task ApplyMigrationsIfEnabledAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
            return;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count == 0)
        {
            app.Logger.LogInformation("Bekleyen migration yok.");
            return;
        }

        app.Logger.LogInformation("Migration uygulanıyor: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync();
    }
}
