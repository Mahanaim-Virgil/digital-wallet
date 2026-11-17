using DigitalWallet.Api.Domain;
using DigitalWallet.Api.Infrastructure.Idempotency;
using DigitalWallet.Api.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace DigitalWallet.Api.Infrastructure;

public sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        // Infrastructure/Configurations altındaki IEntityTypeConfiguration sınıfları.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WalletDbContext).Assembly);
}
