using System.Net.Http.Json;
using DigitalWallet.Contracts;
using DigitalWallet.Notifications.Data;
using DigitalWallet.Notifications.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace DigitalWallet.Notifications.IntegrationTests;

/// <summary>Karar 6: manuel ack, inbox ile tekrar ayıklama, bozuk mesajın dead-letter kuyruğuna gitmesi.</summary>
[Collection(NotificationsCollection.Name)]
public sealed class WalletEventsConsumerTests(NotificationsFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private sealed record NotificationDto(Guid Id, Guid TransactionId, string Kind, string Message, DateTimeOffset CreatedAtUtc);

    private Task<List<NotificationDto>> GetNotificationsAsync(Guid walletId) =>
        fixture.Client.GetFromJsonAsync<List<NotificationDto>>($"/wallets/{walletId}/notifications")!;

    /// <summary>Tüketici asenkron: koşul sağlanana ya da süre dolana kadar yokla.</summary>
    private async Task<List<NotificationDto>> WaitForNotificationsAsync(Guid walletId, int expectedCount)
    {
        var deadline = DateTime.UtcNow + Timeout;
        List<NotificationDto> notifications;
        do
        {
            notifications = await GetNotificationsAsync(walletId);
            if (notifications.Count >= expectedCount)
                return notifications;
            await Task.Delay(200, TestContext.Current.CancellationToken);
        } while (DateTime.UtcNow < deadline);
        return notifications;
    }

    private static DepositCompleted Deposit(Guid walletId, decimal amount = 100m) =>
        new(Guid.CreateVersion7(), walletId, "TRY", amount, amount, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Deposit_event_creates_one_turkish_notification()
    {
        var walletId = Guid.NewGuid();

        await fixture.PublishAsync(Guid.CreateVersion7(), WalletEventTopology.DepositCompletedType,
            WalletEventTopology.DepositCompletedRoutingKey, Deposit(walletId, 1234.5m));

        var notification = Assert.Single(await WaitForNotificationsAsync(walletId, 1));
        Assert.Equal("DepositReceived", notification.Kind);
        Assert.Equal("Cüzdanınıza 1.234,50 TRY yatırıldı. Yeni bakiye: 1.234,50 TRY.", notification.Message);
    }

    [Fact]
    public async Task Transfer_event_notifies_sender_and_receiver()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var transfer = new TransferCompleted(Guid.CreateVersion7(), from, to, "TRY", 10m, 90m, 10m, DateTimeOffset.UtcNow);

        await fixture.PublishAsync(Guid.CreateVersion7(), WalletEventTopology.TransferCompletedType,
            WalletEventTopology.TransferCompletedRoutingKey, transfer);

        Assert.Equal("TransferSent", Assert.Single(await WaitForNotificationsAsync(from, 1)).Kind);
        Assert.Equal("TransferReceived", Assert.Single(await WaitForNotificationsAsync(to, 1)).Kind);
    }

    [Fact]
    public async Task Redelivered_message_with_same_id_is_processed_only_once()
    {
        var walletId = Guid.NewGuid();
        var messageId = Guid.CreateVersion7();
        var @event = Deposit(walletId);

        // Yayıncının "gönderdi ama işaretleyemeden çöktü" durumu: aynı mesaj iki kez gelir.
        await fixture.PublishAsync(messageId, WalletEventTopology.DepositCompletedType, WalletEventTopology.DepositCompletedRoutingKey, @event);
        await fixture.PublishAsync(messageId, WalletEventTopology.DepositCompletedType, WalletEventTopology.DepositCompletedRoutingKey, @event);

        await WaitForNotificationsAsync(walletId, 1);
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken); // ikinci kopyaya işlenme fırsatı
        Assert.Single(await GetNotificationsAsync(walletId));

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal(1, await db.ProcessedMessages.CountAsync(p => p.MessageId == messageId));
    }

    [Theory]
    [InlineData("WalletDeleted", "{}")]                          // bilinmeyen tür
    [InlineData(WalletEventTopology.DepositCompletedType, "bu json degil")] // bozuk gövde
    public async Task Poison_message_goes_to_dead_letter_queue(string type, string body)
    {
        var messageId = Guid.CreateVersion7().ToString();

        await fixture.PublishRawAsync(messageId, type, WalletEventTopology.DepositCompletedRoutingKey, body);

        BasicGetResult? deadLettered = null;
        var deadline = DateTime.UtcNow + Timeout;
        while (deadLettered is null && DateTime.UtcNow < deadline)
        {
            var next = await fixture.Channel.BasicGetAsync(WalletEventsConsumer.DeadLetterQueue, autoAck: true);
            if (next is null)
                await Task.Delay(200, TestContext.Current.CancellationToken);
            else if (next.BasicProperties.MessageId == messageId)
                deadLettered = next;
        }

        Assert.NotNull(deadLettered);
        Assert.Equal(type, deadLettered.BasicProperties.Type);
    }

    [Fact]
    public async Task Migrations_are_applied_and_model_has_no_forgotten_migration()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
