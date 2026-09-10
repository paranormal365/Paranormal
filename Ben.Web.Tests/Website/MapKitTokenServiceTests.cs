using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ben.Web.Website.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// The token MapKit JS hands to Apple. Apple's checks are the specification, so each is a test.
/// </summary>
public class MapKitTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static (MapKitSigningOptions Options, ECDsa PublicKey) Configured()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        return (new MapKitSigningOptions("5778H75249", "623JTDWHAQ", pem), verifier);
    }

    private static (JsonElement Header, JsonElement Claims, string SigningInput, byte[] Signature) Parse(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        static byte[] FromBase64Url(string s) =>
            Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        return (
            JsonDocument.Parse(FromBase64Url(parts[0])).RootElement,
            JsonDocument.Parse(FromBase64Url(parts[1])).RootElement,
            $"{parts[0]}.{parts[1]}",
            FromBase64Url(parts[2]));
    }

    [Fact]
    public void TheTokenIsAnEs256JwtNamingTheKey()
    {
        var (options, _) = Configured();
        using var service = new MapKitTokenService(options);

        var (header, _, _, _) = Parse(service.Issue("https://ishaunted.com", Now));

        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal("623JTDWHAQ", header.GetProperty("kid").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
    }

    [Fact]
    public void TheClaimsNameTheTeamTheOriginAndAThirtyMinuteLife()
    {
        var (options, _) = Configured();
        using var service = new MapKitTokenService(options);

        var (_, claims, _, _) = Parse(service.Issue("https://ishaunted.com/", Now));

        Assert.Equal("5778H75249", claims.GetProperty("iss").GetString());
        Assert.Equal("https://ishaunted.com", claims.GetProperty("origin").GetString());   // no trailing slash
        Assert.Equal(Now.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(Now.AddMinutes(30).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());
    }

    /// <summary>
    /// The signature is the raw r||s form JOSE specifies. .NET's default is DER, which Apple
    /// refuses with a message that says nothing about encoding.
    /// </summary>
    [Fact]
    public void TheSignatureVerifiesInTheRawJoseForm()
    {
        var (options, publicKey) = Configured();
        using var service = new MapKitTokenService(options);

        var (_, _, signingInput, signature) = Parse(service.Issue("http://localhost:5078", Now));

        Assert.Equal(64, signature.Length);   // 32 bytes of r, 32 of s
        Assert.True(publicKey.VerifyData(
            Encoding.ASCII.GetBytes(signingInput), signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void AnOriginWithAPathIsRefused()
    {
        var (options, _) = Configured();
        using var service = new MapKitTokenService(options);

        Assert.Throws<ArgumentException>(() => service.Issue("https://ishaunted.com/places", Now));
        Assert.Throws<ArgumentException>(() => service.Issue("ishaunted.com", Now));
    }

    [Fact]
    public void UnconfiguredMeansNoTokenAndNoException()
    {
        using var service = new MapKitTokenService(MapKitSigningOptions.Unconfigured);

        Assert.False(service.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => service.Issue("https://ishaunted.com", Now));
    }

    /// <summary>A wrong key is a deployment mistake, and says what it is at startup.</summary>
    [Fact]
    public void AKeyThatIsNotAnEcPrivateKeyIsRefusedAtConstruction()
    {
        var rsa = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new MapKitTokenService(new MapKitSigningOptions("T", "K", rsa)));
        Assert.Contains("PKCS#8 EC private key", ex.Message);

        var ex2 = Assert.Throws<InvalidOperationException>(() =>
            new MapKitTokenService(new MapKitSigningOptions("T", "K", "not a key at all")));
        Assert.Contains("PKCS#8 EC private key", ex2.Message);
    }

    /// <summary>The provider switch never hands a page a map that cannot initialise.</summary>
    [Theory]
    [InlineData(Ben.Web.Website.Library.Kit.Maps.MapProvider.Apple,   true,  Ben.Web.Website.Library.Kit.Maps.MapProvider.Apple)]
    [InlineData(Ben.Web.Website.Library.Kit.Maps.MapProvider.Apple,   false, Ben.Web.Website.Library.Kit.Maps.MapProvider.Telerik)]
    [InlineData(Ben.Web.Website.Library.Kit.Maps.MapProvider.Telerik, true,  Ben.Web.Website.Library.Kit.Maps.MapProvider.Telerik)]
    public void AppleIsUsedOnlyWhenChosenAndConfigured(
        Ben.Web.Website.Library.Kit.Maps.MapProvider chosen, bool configured, Ben.Web.Website.Library.Kit.Maps.MapProvider expected)
    {
        Assert.Equal(expected, new Ben.Web.Website.Library.Kit.Maps.MapsOptions(chosen, configured).Effective);
    }
}
