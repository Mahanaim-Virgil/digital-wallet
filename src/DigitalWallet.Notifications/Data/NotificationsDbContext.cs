using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DigitalWallet.Notifications.Data;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string ProcessedMessagesPrimaryKey = "pk_processed_messages";

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.Kind).HasConversion<string>().HasMaxLength(30);
            notification.Property(n => n.Message).HasMaxLength(500).IsRequired();
            notification.HasIndex(n => new { n.WalletId, n.CreatedAtUtc });
        });

        modelBuilder.Entity<ProcessedMessage>(processed =>
        {
            processed.HasKey(p => p.MessageId).HasName(ProcessedMessagesPrimaryKey);
            processed.Property(p => p.MessageId).ValueGeneratedNever();
            processed.Property(p => p.Type).HasMaxLength(100).IsRequired();
        });
    }
}

/// <summary>Yalnızca `dotnet ef` için (Program.cs'i çalıştırmadan DbContext kurar).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__notificationsdb")
            ?? "Host=localhost;Port=5432;Database=notificationsdb";

        return new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
    }
}
