using DigitalWallet.Api.Infrastructure;
using DigitalWallet.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalWallet.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MigrationTests(ApiFixture fixture)
{
    [Fact]
    public async Task All_migrations_are_applied_and_model_has_no_forgotten_migration()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WalletDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        // Kodda model değişti ama `dotnet ef migrations add` unutulduysa burada yakalanır.
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
