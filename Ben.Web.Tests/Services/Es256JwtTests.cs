using System.Security.Cryptography;
using System.Text;
using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>The one signer behind all three Apple tokens.</summary>
public class Es256JwtTests
{
    [Fact]
    public void SignsInTheRawJoseFormThatVerifies()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var imported = Es256Jwt.ImportP256(key.ExportPkcs8PrivateKeyPem(), "Test:Key");

        var jwt = Es256Jwt.Sign(imported, "{\"alg\":\"ES256\"}", "{\"iss\":\"T\"}");
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        var sig = Convert.FromBase64String(parts[2].Replace('-', '+').Replace('_', '/').PadRight(parts[2].Length + (4 - parts[2].Length % 4) % 4, '='));
        Assert.Equal(64, sig.Length);
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), sig, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void AWrongKeyNamesTheSettingThatHoldsIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Es256Jwt.ImportP256("not a key", "Maps:PrivateKey"));
        Assert.StartsWith("Maps:PrivateKey is not a PKCS#8 EC private key", ex.Message);
        var rsa = Assert.Throws<InvalidOperationException>(() => Es256Jwt.ImportP256(RSA.Create(2048).ExportPkcs8PrivateKeyPem(), "Apple:PrivateKey"));
        Assert.StartsWith("Apple:PrivateKey is not a PKCS#8 EC private key", rsa.Message);
    }

    /// <summary>
    /// A 384-bit key is a real EC key, but not one Apple issues. The message says which, rather
    /// than calling it "not a key".
    /// </summary>
    [Fact]
    public void AnotherCurveIsRefusedByName()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var ex = Assert.Throws<InvalidOperationException>(() => Es256Jwt.ImportP256(p384.ExportPkcs8PrivateKeyPem(), "Apns:PrivateKeyPath"));
        Assert.Equal("Apns:PrivateKeyPath is a 384-bit key; Apple issues P-256 keys and requires ES256.", ex.Message);
    }

    /// <summary>
    /// The key is taken apart in managed code (no Windows key store, which shared hosting cannot
    /// use), so the imported key must be the SAME key: what it signs, the original verifies — for
    /// Apple's PKCS#8 form and for the SEC 1 form a converted key may arrive in.
    /// </summary>
    [Theory]
    [InlineData("pkcs8")]
    [InlineData("sec1")]
    public void TheImportedKeyIsTheSameKey(string form)
    {
        using var original = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = form == "pkcs8" ? original.ExportPkcs8PrivateKeyPem() : original.ExportECPrivateKeyPem();
        using var imported = Es256Jwt.ImportP256(pem, "Maps:PrivateKey");

        var data = Encoding.ASCII.GetBytes("header.claims");
        var sig = imported.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.True(original.VerifyData(data, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Equal(original.ExportParameters(false).Q.X, imported.ExportParameters(false).Q.X);
    }

    /// <summary>Surrounding whitespace, as a file copied through a web file manager often gains.</summary>
    [Fact]
    public void WhitespaceAroundTheFileIsFine()
    {
        using var original = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var imported = Es256Jwt.ImportP256("\r\n  " + original.ExportPkcs8PrivateKeyPem().Replace("\n", "\r\n") + "\r\n\r\n", "Maps:PrivateKey");
        Assert.Equal(256, imported.KeySize);
    }
}
