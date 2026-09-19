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
}
