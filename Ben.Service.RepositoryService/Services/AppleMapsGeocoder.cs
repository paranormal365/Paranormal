using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ben.Data.Common.Helpers;
using static Ben.Service.RepositoryService.Services.AddressGeocodingService;

namespace Ben.Service.RepositoryService.Services;

/// <summary>
/// Geocoding through the Apple Maps Server API (item 230): forward from an address or a query,
/// and reverse from a coordinate.
/// </summary>
/// <remarks>
/// <para><b>Two tokens.</b> A Maps auth token — the same ES256 JWT MapKit JS uses on the website,
/// signed with the same Maps key — buys a thirty-minute access token from <c>/v1/token</c>, and
/// that is what every lookup carries. The access token is cached and refreshed a minute early;
/// a 401 on a lookup throws it away and tries once more, because Apple can revoke early.</para>
///
/// <para><b>Nothing here throws.</b> A lookup that fails for any reason answers
/// <see cref="GeocodingLookupResult.Empty"/> exactly as the Geocodio version did, so the eight
/// callers that store coordinates or explain their absence keep their contract. What Apple sent
/// is kept as <c>RawResponseJson</c> where the address tables record it.</para>
///
/// <para><b>Quota, not a bill.</b> Apple allows 25,000 service calls a day on the developer
/// membership, shared with the website's maps; over that it is a request to Apple, not an
/// invoice. The rate limit on the anonymous search endpoint stays for exactly that reason.</para>
/// </remarks>
public sealed class AppleMapsGeocoder
{
    public const string DefaultBaseUrl = "https://maps-api.apple.com/";
    private static readonly TimeSpan AuthTokenLifetime = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly ECDsa _key;
    private readonly string _teamId;
    private readonly string _keyId;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpires;

    /// <param name="privateKeyPem">The Maps key's <c>.p8</c> text. Never stored anywhere but the key file.</param>
    /// <param name="handler">A transport for tests; null means the network.</param>
    public AppleMapsGeocoder(string teamId, string keyId, string privateKeyPem,
        string? baseUrl = null, HttpMessageHandler? handler = null, Func<DateTimeOffset>? now = null)
    {
        _teamId = teamId;
        _keyId  = keyId;
        _key    = Es256Jwt.ImportP256(privateKeyPem, "Maps:PrivateKey");
        _now    = now ?? (() => DateTimeOffset.UtcNow);
        _http   = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("IsHaunted/1.0");
    }

    // ── Forward ───────────────────────────────────────────────────────────────

    public async Task<GeocodingLookupResult> ResolveAsync(
        string streetAddress1, string? streetAddress2, string city, string state, string zipCode, string country,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(streetAddress1) || string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(zipCode) || string.IsNullOrWhiteSpace(country))
            return GeocodingLookupResult.Empty;

        var query = string.Join(", ",
            new[] { streetAddress1, streetAddress2, city, state, zipCode }
                .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
        return await GeocodeAsync(query, CountryCode(country), ct);
    }

    public Task<GeocodingLookupResult> ResolveFromQueryAsync(string query, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(query) ? Task.FromResult(GeocodingLookupResult.Empty)
                                         : GeocodeAsync(query.Trim(), "US", ct);

    private async Task<GeocodingLookupResult> GeocodeAsync(string query, string? countryCode, CancellationToken ct)
    {
        try
        {
            var path = $"v1/geocode?q={Uri.EscapeDataString(query)}&lang=en-US";
            if (countryCode is not null) path += $"&limitToCountries={countryCode}";

            var (ok, json) = await GetAsync(path, ct);
            if (!ok) return GeocodingLookupResult.Empty;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return new GeocodingLookupResult(null, null, json, null);

            var first = results[0];
            var coordinate = first.GetProperty("coordinate");
            return new GeocodingLookupResult(
                coordinate.GetProperty("latitude").GetDecimal(),
                coordinate.GetProperty("longitude").GetDecimal(),
                json,
                Classify(first));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return GeocodingLookupResult.Empty;
        }
    }

    // ── Reverse ───────────────────────────────────────────────────────────────

    public async Task<ReverseGeocodingResult> ReverseAsync(double latitude, double longitude, CancellationToken ct)
    {
        try
        {
            var loc = $"{latitude.ToString("G", CultureInfo.InvariantCulture)},{longitude.ToString("G", CultureInfo.InvariantCulture)}";
            var (ok, json) = await GetAsync($"v1/reverseGeocode?loc={Uri.EscapeDataString(loc)}&lang=en-US", ct);
            if (!ok) return ReverseGeocodingResult.Empty;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return ReverseGeocodingResult.Empty;

            var first = results[0];
            var address = first.TryGetProperty("structuredAddress", out var sa) ? sa : default;
            string? Get(string name) =>
                address.ValueKind == JsonValueKind.Object && address.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;

            var street = Get("fullThoroughfare")
                      ?? string.Join(" ", new[] { Get("subThoroughfare"), Get("thoroughfare") }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new ReverseGeocodingResult(
                string.IsNullOrWhiteSpace(street) ? null : street,
                Get("locality"),
                Get("administrativeAreaCode") ?? Get("administrativeArea"),
                Get("postCode"),
                first.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ReverseGeocodingResult.Empty;
        }
    }

    // ── What kind of answer this is ───────────────────────────────────────────

    /// <summary>
    /// The precision of a hit, in the words the address tables already store from the previous
    /// provider: <c>rooftop</c> for a numbered building, <c>street</c> for a road, <c>place</c>
    /// for a town, a landmark or anything vaguer.
    /// </summary>
    internal static string Classify(JsonElement result)
    {
        if (!result.TryGetProperty("structuredAddress", out var sa) || sa.ValueKind != JsonValueKind.Object) return "place";
        var hasNumber = sa.TryGetProperty("subThoroughfare", out var n) && n.ValueKind == JsonValueKind.String;
        var hasStreet = sa.TryGetProperty("thoroughfare", out var t) && t.ValueKind == JsonValueKind.String;
        return hasNumber && hasStreet ? "rooftop" : hasStreet ? "street" : "place";
    }

    /// <summary>Apple wants ISO codes; the address forms have "US", "USA" or "United States".</summary>
    public static string? CountryCode(string country)
    {
        var c = country.Trim();
        if (c.Length == 2) return c.ToUpperInvariant();
        return c.Equals("USA", StringComparison.OrdinalIgnoreCase) || c.Equals("United States", StringComparison.OrdinalIgnoreCase)
            || c.Equals("United States of America", StringComparison.OrdinalIgnoreCase) ? "US" : null;
    }

    // ── Transport ─────────────────────────────────────────────────────────────

    private async Task<(bool Ok, string Body)> GetAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var access = await AccessTokenAsync(force: attempt > 0, ct);
            if (access is null) return (false, string.Empty);

            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) continue;   // revoked early: once more with a fresh one
            return (response.IsSuccessStatusCode, body);
        }
        return (false, string.Empty);
    }

    private async Task<string?> AccessTokenAsync(bool force, CancellationToken ct)
    {
        if (!force && _accessToken is not null && _accessTokenExpires > _now().AddMinutes(1)) return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!force && _accessToken is not null && _accessTokenExpires > _now().AddMinutes(1)) return _accessToken;

            var now = _now();
            var header = JsonSerializer.Serialize(new { alg = "ES256", kid = _keyId, typ = "JWT" });
            var claims = JsonSerializer.Serialize(new { iss = _teamId, iat = now.ToUnixTimeSeconds(), exp = now.Add(AuthTokenLifetime).ToUnixTimeSeconds() });
            var authToken = Es256Jwt.Sign(_key, header, claims);

            using var request = new HttpRequestMessage(HttpMethod.Get, "v1/token");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { _accessToken = null; return null; }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            _accessToken = doc.RootElement.GetProperty("accessToken").GetString();
            var seconds = doc.RootElement.TryGetProperty("expiresInSeconds", out var e) ? e.GetInt32() : 1800;
            _accessTokenExpires = now.AddSeconds(seconds);
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }
}
