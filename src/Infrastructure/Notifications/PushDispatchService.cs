using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Notifications.Domain;

namespace RegionHR.Infrastructure.Notifications;

/// <summary>
/// Fans a <see cref="PushPayload"/> out to every active <see cref="PushSubscription"/>
/// belonging to an employee, and prunes subscriptions the push service reports as gone.
/// Uses <see cref="IDbContextFactory{TContext}"/> directly (no HTTP) so it works from both
/// Blazor components and background jobs.
/// </summary>
public sealed class PushDispatchService
{
    private readonly IDbContextFactory<RegionHRDbContext> _dbFactory;
    private readonly WebPushSender _sender;
    private readonly ILogger<PushDispatchService> _logger;

    public PushDispatchService(
        IDbContextFactory<RegionHRDbContext> dbFactory,
        WebPushSender sender,
        ILogger<PushDispatchService> logger)
    {
        _dbFactory = dbFactory;
        _sender = sender;
        _logger = logger;
    }

    /// <summary>
    /// Push a payload to all active devices for <paramref name="anstallId"/>.
    /// Returns the number of devices the push service accepted.
    /// </summary>
    public async Task<int> DispatchAsync(Guid anstallId, PushPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (anstallId == Guid.Empty)
        {
            return 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var subscriptions = await db.PushSubscriptions
            .Where(p => p.AnstallId == anstallId && p.ArAktiv)
            .ToListAsync(ct);

        if (subscriptions.Count == 0)
        {
            return 0;
        }

        var json = payload.ToJson();
        var sent = 0;
        var pruned = false;

        foreach (var subscription in subscriptions)
        {
            var result = await _sender.SendAsync(subscription, json, ct);
            if (result == PushSendResult.Sent)
            {
                sent++;
            }
            else if (result == PushSendResult.Expired)
            {
                // Subscription is gone (404/410) — deactivate so we stop trying it.
                subscription.Avaktivera();
                pruned = true;
            }
        }

        if (pruned)
        {
            await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Dispatched web push to {Sent}/{Total} device(s) for employee {Employee}",
            sent, subscriptions.Count, anstallId);

        return sent;
    }

    /// <summary>
    /// Push a persisted <see cref="Notification"/> to the recipient's subscribed devices,
    /// so an in-app notification is also delivered as a Web Push.
    /// </summary>
    public Task<int> DispatchAsync(Notification notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return DispatchAsync(notification.UserId, PushPayload.FromNotification(notification), ct);
    }
}
