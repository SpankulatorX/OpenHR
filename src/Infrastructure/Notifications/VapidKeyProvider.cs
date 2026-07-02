using Microsoft.Extensions.Configuration;
using WebPush;

namespace RegionHR.Infrastructure.Notifications;

/// <summary>
/// Supplies the VAPID (Voluntary Application Server Identification, RFC 8292) key pair
/// used to authenticate Web Push messages.
///
/// <para>
/// ⚠️ DEMO KEYS: the <c>Demo*</c> constants below are a real, valid P-256 key pair that
/// ships with the demo so push works out of the box on localhost. They are PUBLIC in the
/// source tree and MUST NOT be used in production. In production, set your own key pair via
/// configuration (<c>WebPush:PublicKey</c> / <c>WebPush:PrivateKey</c>) — the public key can
/// live in appsettings, the private key belongs in a secret store / environment variable.
/// Generate a fresh pair with <see cref="WebPush.VapidHelper.GenerateVapidKeys"/>.
/// </para>
/// </summary>
public sealed class VapidKeyProvider
{
    /// <summary>Contact for the push service; a mailto: or https: URL per RFC 8292.</summary>
    public const string DemoSubject = "mailto:noreply@openhr.se";

    /// <summary>⚠️ DEMO ONLY — base64url P-256 public key (65-byte uncompressed point).</summary>
    public const string DemoPublicKey = "BMK8fLgyA2D-a9cZBF1nE61YjqiFb96L_WVojktuFbJl2Lup-wHKfBKtAVHy5Uos_5id5O11cfZs7JhSkNVCqFM";

    /// <summary>⚠️ DEMO ONLY — base64url P-256 private key (32-byte scalar). Replace in production.</summary>
    public const string DemoPrivateKey = "j5m9KAKpZU7bzpPsXcp6q3MCRp0Wnr18rZ16Q9-Ue1U";

    public string Subject { get; }
    public string PublicKey { get; }
    public string PrivateKey { get; }

    /// <summary>True when the built-in demo key pair is in use (no production keys configured).</summary>
    public bool IsUsingDemoKeys { get; }

    /// <summary>DI constructor — reads keys from configuration, falling back to the demo pair.</summary>
    public VapidKeyProvider(IConfiguration configuration)
        : this(
            configuration["WebPush:Subject"],
            configuration["WebPush:PublicKey"],
            configuration["WebPush:PrivateKey"])
    {
    }

    /// <summary>
    /// Explicit constructor (used by the DI constructor above and by tests via InternalsVisibleTo).
    /// Kept <c>internal</c> so DI has exactly one public constructor to choose. Any null/blank public
    /// or private key falls back to the full demo pair so the two halves never mismatch.
    /// </summary>
    internal VapidKeyProvider(string? subject, string? publicKey, string? privateKey)
    {
        Subject = string.IsNullOrWhiteSpace(subject) ? DemoSubject : subject;

        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            PublicKey = DemoPublicKey;
            PrivateKey = DemoPrivateKey;
            IsUsingDemoKeys = true;
        }
        else
        {
            PublicKey = publicKey;
            PrivateKey = privateKey;
            IsUsingDemoKeys = false;
        }
    }

    /// <summary>Build the <see cref="VapidDetails"/> passed to the WebPush client per send.</summary>
    public VapidDetails CreateVapidDetails() => new(Subject, PublicKey, PrivateKey);

    /// <summary>
    /// Build the signed VAPID request headers for a push endpoint audience (its scheme+host).
    /// This is pure ES256 JWT signing — it performs NO network I/O — and throws if the key
    /// pair is malformed, which makes it the ideal way to validate keys in a unit test.
    /// </summary>
    /// <param name="audience">Origin of the push endpoint, e.g. <c>https://fcm.googleapis.com</c>.</param>
    public IReadOnlyDictionary<string, string> CreateSignedHeaders(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return VapidHelper.GetVapidHeaders(audience, Subject, PublicKey, PrivateKey);
    }
}
