using DigitalWallet.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Api.Infrastructure.Configurations;

public sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> transaction)
    {
        transaction.ToTable("ledger_transactions", table =>
            table.HasCheckConstraint("ck_ledger_transactions_amount_positive", "amount > 0"));

        transaction.HasKey(t => t.Id);
        transaction.Property(t => t.Id).ValueGeneratedNever();
        transaction.Property(t => t.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        transaction.Property(t => t.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        transaction.Property(t => t.Amount).HasPrecision(19, 4);
        transaction.Property(t => t.CreatedAtUtc).IsRequired();

        // Kayıtlar dışarıya salt okunur liste olarak açılıyor; EF `_entries` alanını dolduruyor.
        transaction.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
        transaction.Navigation(t => t.Entries).HasField("_entries");
    }
}
