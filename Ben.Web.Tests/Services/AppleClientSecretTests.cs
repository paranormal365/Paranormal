using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ben.Data.WebApi.Services.Apple;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>The client secret Apple's token and revoke endpoints check. Apple's checks are the specification.</summary>
public class AppleClientSecretTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    internal static (AppleSigningOptions Options, ECDsa PublicKey) Configured()
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(key.ExportSubjectPublicKeyInfo(), out _);
        return (new AppleSigningOptions("5778H75249", "5VY456C8RR", key.ExportPkcs8PrivateKeyPem()), verifier);
    }

    private static (JsonElement Header, JsonElement Claims, string SigningInput, byte[] Signature) Parse(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        static byte[] FromBase64Url(string s) =>
            Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        return (JsonDocument.Parse(FromBase64Url(parts[0])).RootElement,
                JsonDocument.Parse(FromBase64Url(parts[1])).RootElement,
                $"{parts[0]}.{parts[1]}", FromBase64Url(parts[2]));
    }

    [Fact]
    public void TheSecretNamesTheKeyTheTeamTheClientAndApple()
    {
        var (options, _) = Configured();
        using var secret = new AppleClientSecret(options);

        var (header, claims, _, _) = Parse(secret.Issue("com.ishaunted.ios", Now));

        Assert.Equal("ES256", header.GetProperty("alg").GetString());
        Assert.Equal("5VY456C8RR", header.GetProperty("kid").GetString());
        Assert.Equal("5778H75249", claims.GetProperty("iss").GetString());
        Assert.Equal("com.ishaunted.ios", claims.GetProperty("sub").GetString());
        Assert.Equal("https://appleid.apple.com", claims.GetProperty("aud").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(Now.AddMinutes(10).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());
    }

    /// <summary>Raw r||s, not DER: Apple answers invalid_client to DER and says nothing about why.</summary>
    [Fact]
    public void TheSignatureVerifiesInTheRawJoseForm()
    {
        var (options, publicKey) = Configured();
        using var secret = new AppleClientSecret(options);

        var (_, _, signingInput, signature) = Parse(secret.Issue("com.ishaunted.ios", Now));

        Assert.Equal(64, signature.Length);
        Assert.True(publicKey.VerifyData(Encoding.ASCII.GetBytes(signingInput), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    /// <summary>One secret per client: the website's Services ID and the app's bundle id are different clients to Apple.</summary>
    [Fact]
    public void EachClientGetsItsOwnSubject()
    {
        var (options, _) = Configured();
        using var secret = new AppleClientSecret(options);

        Assert.Equal("com.ishaunted.web",
            Parse(secret.Issue("com.ishaunted.web", Now)).Claims.GetProperty("sub").GetString());
        Assert.Throws<ArgumentException>(() => secret.Issue("", Now));
    }

    [Fact]
    public void UnconfiguredSignsNothingAndThrowsNothingAtConstruction()
    {
        using var secret = new AppleClientSecret(AppleSigningOptions.Unconfigured);
        Assert.False(secret.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => secret.Issue("com.ishaunted.ios", Now));
    }

    [Fact]
    public void AWrongKeyIsADeploymentMistakeAndSaysSoAtStartup()
    {
        var rsa = RSA.Create(2048).ExportPkcs8PrivateKeyPem();
        var ex = Assert.Throws<InvalidOperationException>(() => new AppleClientSecret(new AppleSigningOptions("T", "K", rsa)));
        Assert.Contains("PKCS#8 EC private key", ex.Message);
    }
}
