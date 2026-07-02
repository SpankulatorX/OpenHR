using System.Net;
using Microsoft.Extensions.Logging;
using WebPush;
using DomainPushSubscription = RegionHR.Notifications.Domain.PushSubscription;

namespace RegionHR.Infrastructure.Notifications;

/// <summary>Outcome of a single Web Push send attempt.</summary>
public enum PushSendResult
{
    /// <summary>The push service accepted the message.</summary>
    Sent,

    /// <summary>The subscription is gone (HTTP 404/410) and should be deactivated.</summary>
    Expired,

    /// <summary>A transient or configuration error occurred; the subscription is kept.</summary>
    Failed
}

/// <summary>
/// Sends encrypted Web Push messages to a subscription endpoint using the FOSS
/// <c>WebPush</c> library (aes128gcm content encoding + VAPID auth). This is the only
/// component that performs the actual network call; everything above it (payload
/// building, VAPID keys, fan-out) is pure and unit-tested.
/// </summary>
public sealed class WebPushSender
{
    private readonly VapidKeyProvider _vapid;
    private readonly ILogger<WebPushSender> _logger;
    private readonly WebPushClient _client;

    public WebPushSender(VapidKeyProvider vapid, ILogger<WebPushSender> logger)
    {
        _vapid = vapid;
        _logger = logger;
        _client = new WebPushClient();
    }

    /// <summary>Send a payload to a persisted domain subscription.</summary>
    public Task<PushSendResult> SendAsync(DomainPushSubscription subscription, string payloadJson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return SendAsync(subscription.Endpoint, subscription.P256dhKey, subscription.AuthKey, payloadJson, ct);
    }

    /// <summary>Send a payload to a raw endpoint + browser-supplied keys (base64url).</summary>
    public async Task<PushSendResult> SendAsync(string endpoint, string p256dh, string auth, string payloadJson, CancellationToken ct = default)
    {
        var subscription = new WebPush.PushSubscription(endpoint, p256dh, auth);

        try
        {
            await _client.SendNotificationAsync(subscription, payloadJson, _vapid.CreateVapidDetails(), ct);
            return PushSendResult.Sent;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            _logger.LogInformation(
                "Web push subscription expired (HTTP {Status}) for endpoint {Endpoint}",
                (int)ex.StatusCode, endpoint);
            return PushSendResult.Expired;
        }
        catch (WebPushException ex)
        {
            _logger.LogWarning(
                ex, "Web push failed (HTTP {Status}) for endpoint {Endpoint}",
                (int)ex.StatusCode, endpoint);
            return PushSendResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web push send error for endpoint {Endpoint}", endpoint);
            return PushSendResult.Failed;
        }
    }
}
