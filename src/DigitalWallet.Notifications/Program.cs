using DigitalWallet.Notifications.Data;
using DigitalWallet.Notifications.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health check, service discovery (API ile aynı ortak ayarlar).
builder.AddServiceDefaults();

// Kendi veritabanı (notificationsdb): servisler veri paylaşmaz.
builder.AddNpgsqlDbContext<NotificationsDbContext>("notificationsdb",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

// RabbitMQ bağlantısı; health check açık: RabbitMQ yoksa bu servis işini yapamaz.
builder.AddRabbitMQClient("messaging");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<WalletEventProcessor>();
builder.Services.AddHostedService<WalletEventsConsumer>();

var app = builder.Build();

// Yalnızca AppHost (yerel geliştirme) açar; API'deki karar 2 ile aynı.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

// Doğrulama için: bir cüzdanın son bildirimleri (yeniden eskiye).
app.MapGet("/wallets/{walletId:guid}/notifications", async (Guid walletId, NotificationsDbContext db, CancellationToken ct) =>
    await db.Notifications.AsNoTracking()
        .Where(n => n.WalletId == walletId)
        .OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
        .Take(50)
        .Select(n => new { n.Id, n.TransactionId, Kind = n.Kind.ToString(), n.Message, n.CreatedAtUtc })
        .ToListAsync(ct));

app.Run();

/// <summary>Entegrasyon testlerinde WebApplicationFactory&lt;Program&gt; için.</summary>
public partial class Program;
