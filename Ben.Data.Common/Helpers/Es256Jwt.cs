using System.Security.Cryptography;
using System.Text;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// The one JWT shape Apple asks this codebase to sign, three times over: the MapKit JS token on
/// the website, the Sign in with Apple client secret on the API, and the Maps Server API auth
/// token behind geocoding.
/// </summary>
/// <remarks>
/// Hand-rolled rather than through a JWT library because the whole of it is a few lines and
/// every line is a fact Apple checks — the one that matters being the signature: the raw
/// <c>r||s</c> form JOSE specifies, where .NET's default is DER, which Apple refuses without
/// saying why. Each caller writes its own claims; this only imports the key and signs.
/// </remarks>
public static class Es256Jwt
{
    /// <summary>
    /// Imports a PKCS#8 P-256 private key — the whole text of Apple's <c>.p8</c> file, BEGIN and
    /// END lines included. Throws an <see cref="InvalidOperationException"/> naming
    /// <paramref name="settingName"/> for anything else, because a wrong key is a deployment
    /// mistake and should stop the deploy.
    /// </summary>
    public static ECDsa ImportP256(string pem, string settingName)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(pem);
        }
        catch (Exception ex)
        {
            key.Dispose();
            throw new InvalidOperationException(
                $"{settingName} is not a PKCS#8 EC private key. Apple's .p8 file is the whole text, BEGIN and END lines included.", ex);
        }
        if (key.KeySize != 256)
        {
            var size = key.KeySize;
            key.Dispose();
            throw new InvalidOperationException(
                $"{settingName} is a {size}-bit key; Apple issues P-256 keys and requires ES256.");
        }
        return key;
    }

    /// <summary>Signs <c>header.claims</c> with ES256 and returns the compact JWT.</summary>
    public static string Sign(ECDsa key, string headerJson, string claimsJson)
    {
        var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(headerJson))}.{Base64Url(Encoding.UTF8.GetBytes(claimsJson))}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);   // r||s, what JOSE wants; DER is refused
        return $"{signingInput}.{Base64Url(signature)}";
    }

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
