using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Api.Infrastructure.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public const string PrimaryKeyName = "pk_idempotency_keys";

    public void Configure(EntityTypeBuilder<IdempotencyRecord> record)
    {
        record.ToTable("idempotency_keys");

        // Benzersizlik = idempotency garantisi: aynı (scope, key) ikinci kez yazılamaz.
        record.HasKey(r => new { r.Scope, r.Key }).HasName(PrimaryKeyName);
        record.Property(r => r.Scope).HasMaxLength(IdempotencyRecord.ScopeMaxLength);
        record.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength);
        record.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        record.Property(r => r.CreatedAtUtc).IsRequired();

        record.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(r => r.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
