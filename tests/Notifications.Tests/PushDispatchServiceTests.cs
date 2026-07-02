using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RegionHR.Infrastructure.Notifications;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Notifications.Domain;
using Xunit;

namespace RegionHR.Notifications.Tests;

public class PushDispatchServiceTests
{
    private sealed class TestDbContextFactory(DbContextOptions<RegionHRDbContext> options)
        : IDbContextFactory<RegionHRDbContext>
    {
        public RegionHRDbContext CreateDbContext() => new(options);
    }

    private static PushDispatchService CreateService(out IDbContextFactory<RegionHRDbContext> factory)
    {
        var options = new DbContextOptionsBuilder<RegionHRDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        factory = new TestDbContextFactory(options);
        var vapid = new VapidKeyProvider(subject: null, publicKey: null, privateKey: null);
        var sender = new WebPushSender(vapid, NullLogger<WebPushSender>.Instance);
        return new PushDispatchService(factory, sender, NullLogger<PushDispatchService>.Instance);
    }

    [Fact]
    public async Task DispatchAsync_NoSubscriptions_ReturnsZero_AndSendsNothing()
    {
        var service = CreateService(out _);

        // No subscriptions in the store → short-circuits before any network send.
        var sent = await service.DispatchAsync(Guid.NewGuid(), new PushPayload("Titel", "Text"));

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task DispatchAsync_EmptyEmployeeId_ReturnsZero()
    {
        var service = CreateService(out _);

        var sent = await service.DispatchAsync(Guid.Empty, new PushPayload("Titel", "Text"));

        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task DispatchAsync_OnlyInactiveSubscriptions_ReturnsZero()
    {
        var service = CreateService(out var factory);
        var anstallId = Guid.NewGuid();

        await using (var db = factory.CreateDbContext())
        {
            var sub = PushSubscription.Registrera(anstallId, "https://push.example/abc", "p256dh", "auth");
            sub.Avaktivera();
            db.PushSubscriptions.Add(sub);
            await db.SaveChangesAsync();
        }

        // Only inactive subscriptions exist → nothing is sent.
        var sent = await service.DispatchAsync(anstallId, new PushPayload("Titel", "Text"));

        Assert.Equal(0, sent);
    }
}
