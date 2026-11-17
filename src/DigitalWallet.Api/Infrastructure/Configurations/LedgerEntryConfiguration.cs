using DigitalWallet.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Api.Infrastructure.Configurations;

public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> entry)
    {
        entry.ToTable("ledger_entries", table =>
            table.HasCheckConstraint("ck_ledger_entries_amount_non_zero", "amount <> 0"));

        entry.HasKey(e => e.Id);
        entry.Property(e => e.Id).ValueGeneratedNever();
        entry.Property(e => e.Amount).HasPrecision(19, 4);
        entry.Property(e => e.BalanceAfter).HasPrecision(19, 4);
        entry.Property(e => e.CreatedAtUtc).IsRequired();

        // Kayıt silinmiş bir cüzdana işaret edemez; cüzdan da kaydı olduğu sürece silinemez.
        entry.HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(e => e.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ekstre sorgusu: bir cüzdanın kayıtları, yeniden eskiye.
        entry.HasIndex(e => new { e.WalletId, e.CreatedAtUtc })
            .HasDatabaseName("ix_ledger_entries_wallet_id_created_at_utc");
    }
}
