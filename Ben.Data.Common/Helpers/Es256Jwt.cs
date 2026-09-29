using System.Formats.Asn1;
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
    /// <remarks>
    /// <para><b>Why the key is taken apart here instead of <c>ImportFromPem</c>.</b> On Windows,
    /// importing a PKCS#8 blob goes through CNG's key storage provider, which touches the process
    /// user's profile. Shared IIS hosting runs the site without a loaded profile, and the import
    /// then fails with "The system cannot find the file specified" — a CryptographicException that
    /// says nothing about keys. That stopped the API starting on MonsterASP, 2026-09-29, with a
    /// perfectly good Maps key. Reading the DER here (it is managed code, no key store involved)
    /// and handing the numbers to <c>ECDsa.Create(ECParameters)</c> makes an ephemeral key the
    /// same way on every host.</para>
    /// </remarks>
    public static ECDsa ImportP256(string pem, string settingName)
    {
        ECParameters parameters;
        try
        {
            parameters = ReadP256(pem, settingName);
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            throw NotAnEcKey(settingName, ex);
        }

        try
        {
            return ECDsa.Create(parameters);
        }
        catch (CryptographicException ex)
        {
            throw NotAnEcKey(settingName, ex);
        }
    }

    private const string EcPublicKeyOid = "1.2.840.10045.2.1";
    private const string P256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// The private scalar, and the public point when the file carries it (Apple's always does),
    /// from a PKCS#8 <c>PRIVATE KEY</c> or a SEC 1 <c>EC PRIVATE KEY</c> PEM.
    /// </summary>
    private static ECParameters ReadP256(string pem, string settingName)
    {
        var text = pem ?? string.Empty;
        if (!PemEncoding.TryFind(text, out var fields)) throw NotAnEcKey(settingName, null);
        var label = text[fields.Label];
        var der = Convert.FromBase64String(text[fields.Base64Data].ToString());

        byte[] ecPrivateKey;
        string? curveOid = null;
        if (label.SequenceEqual("PRIVATE KEY"))
        {
            // PrivateKeyInfo ::= SEQUENCE { version INTEGER, algorithm AlgorithmIdentifier,
            //                               privateKey OCTET STRING, ... }
            var info = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            info.ReadInteger();
            var algorithm = info.ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcPublicKeyOid) throw NotAnEcKey(settingName, null);
            if (algorithm.HasData && algorithm.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier))
                curveOid = algorithm.ReadObjectIdentifier();
            ecPrivateKey = info.ReadOctetString();
        }
        else if (label.SequenceEqual("EC PRIVATE KEY"))
        {
            ecPrivateKey = der;
        }
        else
        {
            throw NotAnEcKey(settingName, null);
        }

        // ECPrivateKey ::= SEQUENCE { version INTEGER (1), privateKey OCTET STRING,
        //                             parameters [0] OID OPTIONAL, publicKey [1] BIT STRING OPTIONAL }
        var ec = new AsnReader(ecPrivateKey, AsnEncodingRules.DER).ReadSequence();
        ec.ReadInteger();
        var d = ec.ReadOctetString();
        byte[]? publicPoint = null;
        var parametersTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
        var publicKeyTag = new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true);
        if (ec.HasData && ec.PeekTag().HasSameClassAndValue(parametersTag))
            curveOid ??= ec.ReadSequence(parametersTag).ReadObjectIdentifier();
        if (ec.HasData && ec.PeekTag().HasSameClassAndValue(publicKeyTag))
            publicPoint = ec.ReadSequence(publicKeyTag).ReadBitString(out _);

        if (curveOid != P256Oid)
        {
            throw new InvalidOperationException(
                $"{settingName} is {CurveName(curveOid)}; Apple issues P-256 keys and requires ES256.");
        }
        if (d.Length > 32) throw NotAnEcKey(settingName, null);
        if (d.Length < 32) d = [.. new byte[32 - d.Length], .. d];

        var parameters = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = d };
        // 0x04 || X || Y, uncompressed. Without it the platform derives the point from D.
        if (publicPoint is { Length: 65 } && publicPoint[0] == 0x04)
        {
            parameters.Q = new ECPoint { X = publicPoint[1..33], Y = publicPoint[33..65] };
        }
        return parameters;
    }

    private static string CurveName(string? oid) => oid switch
    {
        "1.3.132.0.34" => "a 384-bit key",
        "1.3.132.0.35" => "a 521-bit key",
        null => "a key that does not name its curve",
        _ => $"a key on curve {oid}",
    };

    private static InvalidOperationException NotAnEcKey(string settingName, Exception? inner) =>
        new($"{settingName} is not a PKCS#8 EC private key. Apple's .p8 file is the whole text, BEGIN and END lines included.", inner);

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
