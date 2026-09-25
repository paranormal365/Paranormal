using System.Security.Cryptography;
using System.Text.Json;
using Ben.Data.Common.Helpers;

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
/// <para>The signing itself is <see cref="Es256Jwt"/>, shared with the two Apple tokens the API
/// signs; the claims here are what MapKit checks.</para>
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

        _key = Es256Jwt.ImportP256(options.PrivateKeyPem, "Maps:PrivateKey");
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

        return Es256Jwt.Sign(_key, header, claims);
    }

    /// <summary>Apple's snapshot service.</summary>
    public const string SnapshotBase = "https://snapshot.apple-mapkit.com";

    /// <summary>
    /// A signed Maps Web Snapshot address: a still picture of a place, fetched by the browser straight from Apple.
    /// </summary>
    /// <remarks>
    /// <para>For the canvas editor's map boxes (canvas plan R35). The picture is never stored by us - Apple's terms
    /// allow map data to be kept only temporarily - so the address expires after <see cref="Lifetime"/> and the
    /// browser's own cache is the only copy.</para>
    ///
    /// <para>Apple's signing: the path and query, with <c>teamId</c> and <c>keyId</c> added, signed ES256 in the raw
    /// r||s form, base64url without padding, and <c>signature</c> appended last. Every value is URL-encoded before
    /// signing, and the order signed is the order sent.</para>
    /// </remarks>
    /// <param name="width">Picture width in points, 50 to 640 (Apple's limits).</param>
    /// <param name="height">Picture height in points, 50 to 640.</param>
    /// <param name="colorScheme">"light" or "dark".</param>
    public string SnapshotUrl(double latitude, double longitude, int zoom, int width, int height, string colorScheme, DateTimeOffset now)
    {
        if (_key is null)
            throw new InvalidOperationException("MapKit signing is not configured.");
        if (latitude is < -90 or > 90 || double.IsNaN(latitude)) throw new ArgumentOutOfRangeException(nameof(latitude));
        if (longitude is < -180 or > 180 || double.IsNaN(longitude)) throw new ArgumentOutOfRangeException(nameof(longitude));
        if (zoom is < 3 or > 20) throw new ArgumentOutOfRangeException(nameof(zoom));
        if (width is < 50 or > 640) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 50 or > 640) throw new ArgumentOutOfRangeException(nameof(height));
        if (colorScheme is not ("light" or "dark")) throw new ArgumentOutOfRangeException(nameof(colorScheme));

        var point = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{latitude:F6},{longitude:F6}");
        var annotations = JsonSerializer.Serialize(new[] { new { point, color = "c0392b", markerStyle = "balloon" } });
        var parameters = new (string Name, string Value)[]
        {
            ("center", point),
            ("z", zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("size", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{width}x{height}")),
            ("scale", "2"),
            ("t", "standard"),
            ("colorScheme", colorScheme),
            ("annotations", annotations),
            ("expires", now.Add(Lifetime).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("teamId", _options.TeamId),
            ("keyId", _options.KeyId),
        };

        var pathAndQuery = "/api/v1/snapshot?" + string.Join("&", parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        var signature = _key.SignData(System.Text.Encoding.UTF8.GetBytes(pathAndQuery), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{SnapshotBase}{pathAndQuery}&signature={Es256Jwt.Base64Url(signature)}";
    }

    public void Dispose() => _key?.Dispose();
}
