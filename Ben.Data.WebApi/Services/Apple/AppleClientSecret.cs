using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ben.Data.WebApi.Services.Apple;

/// <summary>What Apple issued for Sign in with Apple's server side: the key, and who it belongs to.</summary>
/// <param name="TeamId">The developer team, the secret's issuer.</param>
/// <param name="KeyId">The Key ID Apple shows beside the downloaded <c>.p8</c>; goes in the header as <c>kid</c>.</param>
/// <param name="PrivateKeyPem">The <c>.p8</c> contents, a PKCS#8 P-256 key. A secret: never in the repository.</param>
public sealed record AppleSigningOptions(string TeamId, string KeyId, string PrivateKeyPem)
{
    public static readonly AppleSigningOptions Unconfigured = new(string.Empty, string.Empty, string.Empty);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TeamId) && !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(PrivateKeyPem);
}

/// <summary>
/// Signs the client secret Apple's token and revocation endpoints require.
/// </summary>
/// <remarks>
/// <para>Apple does not hand out a static client secret; the secret <i>is</i> a JWT the server
/// signs with the Sign in with Apple key, for a particular client id, for at most six months.
/// Ours lives ten minutes and is minted per call: there is nothing to rotate and nothing worth
/// stealing from a log.</para>
///
/// <para>The claims are what Apple checks: <c>iss</c> the team, <c>sub</c> the client id the
/// authorization code was minted for (the app's bundle id, or the website's Services ID),
/// <c>aud</c> Apple itself, <c>iat</c>/<c>exp</c>. Signed ES256 with the raw <c>r||s</c>
/// signature JOSE specifies — .NET's default is DER, which Apple refuses as <c>invalid_client</c>
/// and says nothing about why. Hand-rolled for the same reason as the MapKit token on the website:
/// forty lines, each of them a fact Apple checks.</para>
/// </remarks>
public sealed class AppleClientSecret : IDisposable
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const string Audience = "https://appleid.apple.com";

    private readonly AppleSigningOptions _options;
    private readonly ECDsa? _key;

    public AppleClientSecret(AppleSigningOptions options)
    {
        _options = options;
        if (!options.IsConfigured) return;

        _key = ECDsa.Create();
        try
        {
            _key.ImportFromPem(options.PrivateKeyPem);
        }
        catch (Exception ex)
        {
            _key.Dispose();
            throw new InvalidOperationException(
                "Apple:PrivateKey is not a PKCS#8 EC private key. Apple's .p8 file is the whole "
              + "text, BEGIN and END lines included.", ex);
        }
        if (_key.KeySize != 256)
        {
            _key.Dispose();
            throw new InvalidOperationException(
                $"Apple:PrivateKey is a {_key.KeySize}-bit key; Apple issues P-256 keys and requires ES256.");
        }
    }

    public bool IsConfigured => _key is not null;

    /// <summary>A secret for <paramref name="clientId"/>, valid from <paramref name="now"/>.</summary>
    public string Issue(string clientId, DateTimeOffset now)
    {
        if (_key is null) throw new InvalidOperationException("Sign in with Apple signing is not configured.");
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("A client id is required.", nameof(clientId));

        var header = JsonSerializer.Serialize(new { alg = "ES256", kid = _options.KeyId, typ = "JWT" });
        var claims = JsonSerializer.Serialize(new
        {
            iss = _options.TeamId,
            iat = now.ToUnixTimeSeconds(),
            exp = now.Add(Lifetime).ToUnixTimeSeconds(),
            aud = Audience,
            sub = clientId,
        });
        var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(claims))}";
        var signature = _key.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => _key?.Dispose();
}
