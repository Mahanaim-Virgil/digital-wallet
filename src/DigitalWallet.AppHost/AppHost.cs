var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL Docker container'ı. Şifreyi Aspire üretir ve AppHost'un user-secrets'ına yazar (repoya girmez).
// Veri, container silinse de kalsın diye isimli bir Docker volume'unda tutulur.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("digital-wallet-postgres-data");

var walletDb = postgres.AddDatabase("walletdb");
// Bildirim servisinin kendi veritabanı: servisler veri paylaşmaz, yalnızca mesajlaşır.
var notificationsDb = postgres.AddDatabase("notificationsdb");

// RabbitMQ container'ı (yönetim arayüzü dahil). Kuyruktaki kalıcı mesajlar volume'da tutulur.
var messaging = builder.AddRabbitMQ("messaging")
    .WithManagementPlugin()
    .WithDataVolume("digital-wallet-rabbitmq-data");

builder.AddProject<Projects.DigitalWallet_Api>("api")
    .WithReference(walletDb)          // ConnectionStrings__walletdb ortam değişkenini verir
    .WaitFor(walletDb)                // veritabanı hazır olmadan API başlamaz
    // RabbitMQ'yu bilerek beklemiyoruz: outbox sayesinde API, RabbitMQ kapalıyken de para hareketini kabul eder.
    .WithReference(messaging)
    .WithEnvironment("Database__ApplyMigrationsOnStartup", "true");

builder.AddProject<Projects.DigitalWallet_Notifications>("notifications")
    .WithReference(notificationsDb)
    .WaitFor(notificationsDb)
    .WithReference(messaging)
    .WaitFor(messaging)
    .WithEnvironment("Database__ApplyMigrationsOnStartup", "true");

builder.Build().Run();
