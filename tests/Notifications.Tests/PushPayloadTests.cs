using System.Text.Json;
using RegionHR.Notifications.Domain;
using Xunit;

namespace RegionHR.Notifications.Tests;

public class PushPayloadTests
{
    [Fact]
    public void ToJson_UsesCamelCaseKeys_AndValues()
    {
        var payload = new PushPayload(
            title: "Ledighet godkänd",
            body: "Din ansökan är godkänd.",
            url: "/ledighet",
            tag: "leave-42",
            requireInteraction: true);

        using var doc = JsonDocument.Parse(payload.ToJson());
        var root = doc.RootElement;

        Assert.Equal("Ledighet godkänd", root.GetProperty("title").GetString());
        Assert.Equal("Din ansökan är godkänd.", root.GetProperty("body").GetString());
        Assert.Equal("/ledighet", root.GetProperty("url").GetString());
        Assert.Equal("leave-42", root.GetProperty("tag").GetString());
        Assert.Equal("/favicon.png", root.GetProperty("icon").GetString());
        Assert.True(root.GetProperty("requireInteraction").GetBoolean());
    }

    [Fact]
    public void ToJson_OmitsNullUrlAndTag()
    {
        var payload = new PushPayload("Titel", "Text");

        using var doc = JsonDocument.Parse(payload.ToJson());
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("url", out _));
        Assert.False(root.TryGetProperty("tag", out _));
        Assert.Equal("Titel", root.GetProperty("title").GetString());
    }

    [Fact]
    public void ToJson_PreservesSwedishCharactersLiterally()
    {
        var payload = new PushPayload("Åäö", "Löneöversyn påbörjad");

        var json = payload.ToJson();

        // UnsafeRelaxedJsonEscaping keeps the characters literal (no å escapes).
        Assert.Contains("Åäö", json);
        Assert.Contains("Löneöversyn påbörjad", json);
        Assert.DoesNotContain("\\u00", json);
    }

    [Fact]
    public void Constructor_BlankTitle_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PushPayload("   ", "text"));
    }

    [Fact]
    public void Constructor_BlankIcon_DefaultsToFavicon()
    {
        var payload = new PushPayload("t", "b", icon: "  ");
        Assert.Equal("/favicon.png", payload.Icon);
    }

    [Fact]
    public void FromNotification_MapsFields()
    {
        var notification = Notification.Create(
            Guid.NewGuid(),
            "Godkännande krävs",
            "Ett ärende väntar.",
            NotificationType.Action,
            NotificationChannel.Push,
            actionUrl: "/godkannanden");

        var payload = PushPayload.FromNotification(notification);

        Assert.Equal("Godkännande krävs", payload.Title);
        Assert.Equal("Ett ärende väntar.", payload.Body);
        Assert.Equal("/godkannanden", payload.Url);
        Assert.Equal(notification.Id.ToString(), payload.Tag);
        // Action notifications should stay on screen until interacted with.
        Assert.True(payload.RequireInteraction);
    }

    [Fact]
    public void FromNotification_NullActionUrl_DefaultsToNotiser()
    {
        var notification = Notification.Create(
            Guid.NewGuid(),
            "Info",
            "Ett meddelande.",
            NotificationType.Info);

        var payload = PushPayload.FromNotification(notification);

        Assert.Equal("/notiser", payload.Url);
        Assert.False(payload.RequireInteraction);
    }
}
