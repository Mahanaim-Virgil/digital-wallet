var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL Docker container'ı. Şifreyi Aspire üretir ve AppHost'un user-secrets'ına yazar (repoya girmez).
// Veri, container silinse de kalsın diye isimli bir Docker volume'unda tutulur.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("digital-wallet-postgres-data");

var walletDb = postgres.AddDatabase("walletdb");

builder.AddProject<Projects.DigitalWallet_Api>("api")
    .WithReference(walletDb)          // ConnectionStrings__walletdb ortam değişkenini verir
    .WaitFor(walletDb)                // veritabanı hazır olmadan API başlamaz
    .WithEnvironment("Database__ApplyMigrationsOnStartup", "true");

builder.Build().Run();
