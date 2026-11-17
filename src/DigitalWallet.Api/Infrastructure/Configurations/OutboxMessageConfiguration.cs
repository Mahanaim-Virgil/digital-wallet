using DigitalWallet.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWallet.Api.Infrastructure.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> message)
    {
        message.ToTable("outbox_messages");

        message.HasKey(m => m.Id);
        message.Property(m => m.Id).ValueGeneratedNever();
        message.Property(m => m.Type).HasMaxLength(100).IsRequired();
        message.Property(m => m.RoutingKey).HasMaxLength(100).IsRequired();
        message.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        message.Property(m => m.OccurredAtUtc).IsRequired();
        message.Property(m => m.LastError).HasMaxLength(1000);

        // Yayıncının sorgusu yalnızca bekleyenlere bakar; kısmi index yayınlananlar büyüdükçe küçük kalır.
        message.HasIndex(m => m.Id)
            .HasFilter("published_at_utc IS NULL")
            .HasDatabaseName("ix_outbox_messages_unpublished");
    }
}
