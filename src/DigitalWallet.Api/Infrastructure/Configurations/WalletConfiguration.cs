using DigitalWallet.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Api.Infrastructure.Configurations;

public sealed class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> wallet)
    {
        wallet.ToTable(table =>
            // Son savunma hattı: kod hata yapsa bile müşteri bakiyesi eksiye düşemez.
            table.HasCheckConstraint("ck_wallets_customer_balance_non_negative",
                "type = 'System' OR balance >= 0"));

        wallet.HasKey(w => w.Id);
        // Id uygulamada üretiliyor (Guid v7); veritabanı üretmesin.
        wallet.Property(w => w.Id).ValueGeneratedNever();
        wallet.Property(w => w.OwnerName).HasMaxLength(Wallet.OwnerNameMaxLength).IsRequired();
        // ISO 4217 kodu: her zaman 3 harf.
        wallet.Property(w => w.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        // Enum metin olarak: SQL'de okunur, enum sırası değişse de veri bozulmaz.
        wallet.Property(w => w.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        // Para: kayan nokta değil, sabit hassasiyetli numeric.
        wallet.Property(w => w.Balance).HasPrecision(19, 4);
        wallet.Property(w => w.CreatedAtUtc).IsRequired();
        wallet.Ignore(w => w.IsSystem);

        // Para birimi başına tek sistem cüzdanı.
        wallet.HasIndex(w => w.Currency)
            .IsUnique()
            .HasFilter("type = 'System'")
            .HasDatabaseName("ux_wallets_system_currency");

        var seededAt = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
        wallet.HasData(
            SystemWallet(SystemWallets.Try, "TRY", seededAt),
            SystemWallet(SystemWallets.Usd, "USD", seededAt),
            SystemWallet(SystemWallets.Eur, "EUR", seededAt));
    }

    private static object SystemWallet(Guid id, string currency, DateTimeOffset createdAt) => new
    {
        Id = id,
        OwnerName = $"Sistem: dış kaynak ({currency})",
        Currency = currency,
        Type = WalletType.System,
        Balance = 0m,
        CreatedAtUtc = createdAt,
    };
}
