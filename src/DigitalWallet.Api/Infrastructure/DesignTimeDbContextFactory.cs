using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DigitalWallet.Api.Infrastructure;

/// <summary>
/// Yalnızca `dotnet ef` için. Program.cs'i çalıştırmadan DbContext kurar; böylece Aspire'ın
/// bağlantı dizesi olmadan da `migrations add` yapılabilir (bu komut veritabanına bağlanmaz).
/// `database update` için gerçek bağlantı dizesi `ConnectionStrings__walletdb` ortam değişkeninden okunur.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__walletdb")
            ?? "Host=localhost;Port=5432;Database=walletdb";

        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new WalletDbContext(options);
    }
}
