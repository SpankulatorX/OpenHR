using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RegionHR.Notifications.Domain;

/// <summary>
/// Payload for a single Web Push message (RFC 8030). Serialized to the exact JSON
/// shape the service worker reads in its <c>push</c> event handler
/// (see <c>wwwroot/service-worker.js</c> and <c>wwwroot/sw-push.js</c>):
/// <c>{ title, body, url, tag, icon, requireInteraction }</c>.
/// Pure value type — no I/O, fully unit-testable.
/// </summary>
public sealed class PushPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Omit null url/tag so the SW falls back to its own defaults.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep Swedish characters (å ä ö) literal instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [JsonPropertyName("title")]
    public string Title { get; }

    [JsonPropertyName("body")]
    public string Body { get; }

    [JsonPropertyName("url")]
    public string? Url { get; }

    [JsonPropertyName("tag")]
    public string? Tag { get; }

    [JsonPropertyName("icon")]
    public string Icon { get; }

    [JsonPropertyName("requireInteraction")]
    public bool RequireInteraction { get; }

    [JsonConstructor]
    public PushPayload(
        string title,
        string body,
        string? url = null,
        string? tag = null,
        string icon = "/favicon.png",
        bool requireInteraction = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
        Body = body ?? string.Empty;
        Url = string.IsNullOrWhiteSpace(url) ? null : url;
        Tag = string.IsNullOrWhiteSpace(tag) ? null : tag;
        Icon = string.IsNullOrWhiteSpace(icon) ? "/favicon.png" : icon;
        RequireInteraction = requireInteraction;
    }

    /// <summary>Serialize to the JSON string sent as the encrypted push payload.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Build a push payload from a persisted <see cref="Notification"/> so an in-app
    /// notification can also be delivered to subscribed devices.
    /// </summary>
    public static PushPayload FromNotification(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new PushPayload(
            title: notification.Title,
            body: notification.Message,
            url: string.IsNullOrWhiteSpace(notification.ActionUrl) ? "/notiser" : notification.ActionUrl,
            tag: notification.Id.ToString(),
            requireInteraction: notification.Type == NotificationType.Action);
    }
}
