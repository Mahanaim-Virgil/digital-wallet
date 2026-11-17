using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace DigitalWallet.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Koleksiyon başına gerçek PostgreSQL ve RabbitMQ container'ı (karar 8). Sürümler yerel Aspire ile aynı.
/// Varsayılan API örneğinde outbox yayıncısı kapalı: outbox satırları "bekliyor" durumunda kalır.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4.3-management").Build();

    public ApiFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();
    public string RabbitConnectionString => _rabbit.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        Factory = CreateFactory(publisherEnabled: false);
        // İlk istek uygulamayı başlatır; açılışta migration'lar uygulanır.
        Client = Factory.CreateClient();
    }

    /// <summary>Aynı container'lara bağlı ek bir API örneği (ör. yayıncı açık ya da RabbitMQ adresi bozuk).</summary>
    public ApiFactory CreateFactory(bool publisherEnabled, string? rabbitConnectionString = null) =>
        new(PostgresConnectionString, rabbitConnectionString ?? RabbitConnectionString, publisherEnabled);

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (Factory is not null)
            await Factory.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>API'yi bellekte açar; bağlantı bilgilerini Aspire'ın okuduğu anahtarlara verir.</summary>
public sealed class ApiFactory(string postgres, string rabbit, bool publisherEnabled) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:walletdb", postgres);
        builder.UseSetting("ConnectionStrings:messaging", rabbit);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Outbox:PublisherEnabled", publisherEnabled ? "true" : "false");
    }
}
