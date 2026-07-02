using RegionHR.Infrastructure.Notifications;
using Xunit;

namespace RegionHR.Notifications.Tests;

public class VapidKeyProviderTests
{
    [Fact]
    public void NoKeysConfigured_FallsBackToDemoKeys()
    {
        var provider = new VapidKeyProvider(subject: null, publicKey: null, privateKey: null);

        Assert.True(provider.IsUsingDemoKeys);
        Assert.Equal(VapidKeyProvider.DemoSubject, provider.Subject);
        Assert.Equal(VapidKeyProvider.DemoPublicKey, provider.PublicKey);
        Assert.Equal(VapidKeyProvider.DemoPrivateKey, provider.PrivateKey);
    }

    [Fact]
    public void OnlySubjectConfigured_KeepsSubject_ButUsesDemoKeyPair()
    {
        var provider = new VapidKeyProvider(subject: "mailto:hr@region.se", publicKey: "", privateKey: null);

        Assert.True(provider.IsUsingDemoKeys);
        Assert.Equal("mailto:hr@region.se", provider.Subject);
        Assert.Equal(VapidKeyProvider.DemoPublicKey, provider.PublicKey);
    }

    [Fact]
    public void ProductionKeysConfigured_AreUsed()
    {
        var provider = new VapidKeyProvider(
            subject: "https://openhr.example.se",
            publicKey: "PROD_PUBLIC_KEY",
            privateKey: "PROD_PRIVATE_KEY");

        Assert.False(provider.IsUsingDemoKeys);
        Assert.Equal("https://openhr.example.se", provider.Subject);
        Assert.Equal("PROD_PUBLIC_KEY", provider.PublicKey);
        Assert.Equal("PROD_PRIVATE_KEY", provider.PrivateKey);
    }

    [Fact]
    public void CreateSignedHeaders_WithDemoKeys_ProducesValidVapidHeaders()
    {
        var provider = new VapidKeyProvider(subject: null, publicKey: null, privateKey: null);

        // Pure ES256 JWT signing — no network. Throws if the demo key pair is malformed,
        // so a successful, well-formed result proves the shipped demo keys are valid.
        var headers = provider.CreateSignedHeaders("https://fcm.googleapis.com");

        Assert.True(headers.ContainsKey("Authorization"));
        Assert.StartsWith("WebPush ", headers["Authorization"]);
        Assert.True(headers.ContainsKey("Crypto-Key"));
        Assert.Contains(VapidKeyProvider.DemoPublicKey, headers["Crypto-Key"]);
    }

    [Fact]
    public void CreateSignedHeaders_BlankAudience_Throws()
    {
        var provider = new VapidKeyProvider(subject: null, publicKey: null, privateKey: null);

        Assert.Throws<ArgumentException>(() => provider.CreateSignedHeaders("   "));
    }
}
