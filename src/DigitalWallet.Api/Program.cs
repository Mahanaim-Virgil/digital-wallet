using DigitalWallet.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry, health check, service discovery ve HttpClient dayanıklılık ayarları (ServiceDefaults).
builder.AddServiceDefaults();

// Aspire bileşeni: bağlantı dizesini `ConnectionStrings:walletdb`'den okur (AppHost verir),
// ayrıca DB health check'i, retry ve Npgsql tracing'i ekler.
builder.AddNpgsqlDbContext<WalletDbContext>("walletdb",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

await app.ApplyMigrationsIfEnabledAsync();

// /health ve /alive (yalnızca Development ortamında açılır).
app.MapDefaultEndpoints();

app.Run();
