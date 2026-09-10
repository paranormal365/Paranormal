using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ben.Web.Website.Services;

/// <summary>What Apple issued for the website's Maps ID: the key, and who it belongs to.</summary>
/// <param name="TeamId">The developer team, the token's issuer.</param>
/// <param name="KeyId">The Key ID Apple shows beside the downloaded <c>.p8</c>; goes in the header as <c>kid</c>.</param>
/// <param name="PrivateKeyPem">The <c>.p8</c> contents, a PKCS#8 P-256 key. A secret: never in the repository.</param>
public sealed record MapKitSigningOptions(string TeamId, string KeyId, string PrivateKeyPem)
{
    public static readonly MapKitSigningOptions Unconfigured = new(string.Empty, string.Empty, string.Empty);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TeamId) && !string.IsNullOrWhiteSpace(KeyId) && !string.IsNullOrWhiteSpace(PrivateKeyPem);
}

/// <summary>
/// Signs the short-lived tokens MapKit JS presents to Apple.
/// </summary>
/// <remarks>
/// <para>MapKit JS never holds the private key. The page calls <c>mapkit.init</c> with a callback,
/// the callback fetches a token from this site, and the library sends that token to Apple with
/// every map load. Apple checks the signature against the key registered for the Maps ID, so
/// the key stays on this server and the browser only ever sees something that expires.</para>
///
/// <para><b>The token is a JWT signed with ES256</b>: header <c>{alg, kid, typ}</c>, claims
/// <c>iss</c> (team), <c>iat</c>, <c>exp</c>, and <c>origin</c>. The origin claim is the part
/// worth having: a token naming <c>https://ishaunted.com</c> is refused by Apple on any other
/// site, so a copied token spends nobody's quota but ours, and only from our pages.</para>
///
/// <para>Written by hand rather than through a JWT library because the whole of it is forty
/// lines and every line is a fact about what Apple checks; a library would hide the one that
/// matters (the signature must be the raw <c>r||s</c> form, not DER).</para>
/// </remarks>
public sealed class MapKitTokenService : IDisposable
{
    /// <summary>
    /// Short, because the browser keeps asking. MapKit JS requests a fresh token as one expires,
    /// so a leaked one is worth thirty minutes of somebody's map views and nothing after.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly MapKitSigningOptions _options;
    private readonly ECDsa? _key;

    public MapKitTokenService(MapKitSigningOptions options)
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
                "Maps:PrivateKey is not a PKCS#8 EC private key. Apple's .p8 file is the whole "
              + "text, BEGIN and END lines included.", ex);
        }

        if (_key.KeySize != 256)
        {
            _key.Dispose();
            throw new InvalidOperationException(
                $"Maps:PrivateKey is a {_key.KeySize}-bit key; Apple issues P-256 keys and MapKit JS requires ES256.");
        }
    }

    public bool IsConfigured => _key is not null;

    /// <summary>Issues a token for pages served from <paramref name="origin"/> (scheme and host, no path).</summary>
    public string Issue(string origin, DateTimeOffset now)
    {
        if (_key is null)
            throw new InvalidOperationException("MapKit signing is not configured.");
        if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.PathAndQuery != "/")
            throw new ArgumentException("The origin must be a scheme and host only, such as https://ishaunted.com.", nameof(origin));

        var header = JsonSerializer.Serialize(new { alg = "ES256", kid = _options.KeyId, typ = "JWT" });
        var claims = JsonSerializer.Serialize(new
        {
            iss    = _options.TeamId,
            iat    = now.ToUnixTimeSeconds(),
            exp    = now.Add(Lifetime).ToUnixTimeSeconds(),
            origin = origin.TrimEnd('/'),
        });

        var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header))}.{Base64Url(Encoding.UTF8.GetBytes(claims))}";
        var signature = _key.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);   // r||s, what JOSE wants; DER is refused

        return $"{signingInput}.{Base64Url(signature)}";
    }

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => _key?.Dispose();
}
