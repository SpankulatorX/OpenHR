using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Notifications.Domain;
using RegionHR.Notifications.Services;

namespace RegionHR.Infrastructure.Notifications;

/// <summary>
/// Concrete <see cref="INotificationService"/> that persists an in-app notification and,
/// as part of the same call, fans it out to the recipient's subscribed devices via Web Push.
/// This is the single seam that satisfies "when a Notification is created, also push it":
/// call sites that use this service get Web Push delivery for free. Push failures are
/// swallowed (logged) so they never break the originating workflow.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly PushDispatchService _push;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IDbContextFactory<RegionHRDbContext> dbFactory,
        PushDispatchService push,
        ILogger<NotificationService> logger)
    {
        _dbFactory = dbFactory;
        _push = push;
        _logger = logger;
    }

    public async Task SendAsync(
        Guid userId,
        string title,
        string message,
        NotificationType type = NotificationType.Info,
        NotificationChannel channel = NotificationChannel.InApp,
        string? actionUrl = null,
        CancellationToken ct = default)
    {
        var notification = Notification.Create(userId, title, message, type, channel, actionUrl);

        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
        }

        try
        {
            await _push.DispatchAsync(notification, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web push fan-out failed for notification {NotificationId}", notification.Id);
        }
    }

    public async Task<IReadOnlyList<Notification>> GetUnreadAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, ct);
        if (notification is null)
        {
            return;
        }

        notification.MarkAsRead();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var unread = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);

        foreach (var notification in unread)
        {
            notification.MarkAsRead();
        }

        if (unread.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
