using DigitalWallet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(wallet =>
        {
            wallet.HasKey(w => w.Id);
            // Id uygulamada üretiliyor (Guid v7); veritabanı üretmesin.
            wallet.Property(w => w.Id).ValueGeneratedNever();
            wallet.Property(w => w.OwnerName).HasMaxLength(Wallet.OwnerNameMaxLength).IsRequired();
            // ISO 4217 kodu: her zaman 3 harf.
            wallet.Property(w => w.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            // Para: kayan nokta değil, sabit hassasiyetli numeric.
            wallet.Property(w => w.Balance).HasPrecision(19, 4);
            wallet.Property(w => w.CreatedAtUtc).IsRequired();
        });
    }
}
