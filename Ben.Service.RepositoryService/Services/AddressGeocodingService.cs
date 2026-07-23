using System.Globalization;
using System.Text.Json;

namespace Ben.Service.RepositoryService.Services;

/// <summary>
/// Geocoding service backed by <see href="https://dash.geocod.io"/>.
/// Call <see cref="Configure"/> at startup (e.g. from Program.cs) to
/// provide the API key and base URL read from application configuration.
/// </summary>
public static class AddressGeocodingService
{
    private static readonly HttpClient HttpClient = CreateClient();
    private static string _apiKey  = string.Empty;
    private static string _baseUrl = "https://api.geocod.io/v2/";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BenApp/1.0");
        return client;
    }

    /// <summary>
    /// Initialises the service with credentials from application configuration.
    /// Must be called once at startup before any geocoding requests are made.
    /// </summary>
    public static void Configure(string apiKey, string? baseUrl = null)
    {
        _apiKey  = apiKey ?? string.Empty;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.geocod.io/v2/"
            : baseUrl.TrimEnd('/') + "/";
    }

    // ── Forward geocoding ─────────────────────────────────────────────────────

    public static async Task<GeocodingLookupResult> TryResolveCoordinatesAsync(
        string streetAddress1,
        string? streetAddress2,
        string city,
        string state,
        string zipCode,
        string country,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return GeocodingLookupResult.Empty;

        if (string.IsNullOrWhiteSpace(streetAddress1) ||
            string.IsNullOrWhiteSpace(city) ||
            string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(zipCode) ||
            string.IsNullOrWhiteSpace(country))
        {
            return GeocodingLookupResult.Empty;
        }

        try
        {
            var query = string.Join(", ",
                new[] { streetAddress1, streetAddress2, city, state, zipCode }
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!.Trim()));

            var url = $"{_baseUrl}geocode?q={Uri.EscapeDataString(query)}&api_key={_apiKey}&limit=1";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) return GeocodingLookupResult.Empty;

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.GetArrayLength() == 0)
                return new GeocodingLookupResult(null, null, json, null);

            var first    = results[0];
            var location = first.GetProperty("location");
            var lat      = location.GetProperty("lat").GetDecimal();
            var lng      = location.GetProperty("lng").GetDecimal();
            var type     = first.TryGetProperty("accuracy_type", out var at) ? at.GetString() : null;

            return new GeocodingLookupResult(lat, lng, json, type);
        }
        catch
        {
            return GeocodingLookupResult.Empty;
        }
    }

    /// <summary>
    /// Geocodes a freeform address query string (e.g. "705 Meeting St, Franklin, TN 37064").
    /// Does not require individual address components.
    /// </summary>
    public static async Task<GeocodingLookupResult> TryResolveFromQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(query))
            return GeocodingLookupResult.Empty;

        try
        {
            var url = $"{_baseUrl}geocode?q={Uri.EscapeDataString(query.Trim())}&api_key={_apiKey}&limit=1";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return GeocodingLookupResult.Empty;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return new GeocodingLookupResult(null, null, json, null);

            var first    = results[0];
            var location = first.GetProperty("location");
            var lat      = location.GetProperty("lat").GetDecimal();
            var lng      = location.GetProperty("lng").GetDecimal();
            var type     = first.TryGetProperty("accuracy_type", out var at) ? at.GetString() : null;
            return new GeocodingLookupResult(lat, lng, json, type);
        }
        catch
        {
            return GeocodingLookupResult.Empty;
        }
    }

    public static async Task<ReverseGeocodingResult> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) return ReverseGeocodingResult.Empty;

        try
        {
            var lat = latitude.ToString("G", CultureInfo.InvariantCulture);
            var lon = longitude.ToString("G", CultureInfo.InvariantCulture);
            var url = $"{_baseUrl}reverse?q={Uri.EscapeDataString($"{lat},{lon}")}&api_key={_apiKey}&limit=1";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode) return ReverseGeocodingResult.Empty;

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.GetArrayLength() == 0)
                return ReverseGeocodingResult.Empty;

            var comps = results[0].GetProperty("address_components");

            string? Get(string key) =>
                comps.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;

            var number  = Get("number");
            var street  = Get("street");
            var suffix  = Get("suffix");
            var street1 = string.Join(" ",
                new[] { number, street, suffix }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new ReverseGeocodingResult(
                string.IsNullOrWhiteSpace(street1) ? null : street1,
                Get("city"), Get("state"), Get("zip"), Get("country"));
        }
        catch
        {
            return ReverseGeocodingResult.Empty;
        }
    }

    // ── Result records ────────────────────────────────────────────────────────

    public sealed record GeocodingLookupResult(
        decimal? Latitude,
        decimal? Longitude,
        string?  RawResponseJson,
        string?  ResultType)
    {
        public static GeocodingLookupResult Empty => new(null, null, null, null);
    }

    public sealed record ReverseGeocodingResult(
        string? StreetAddress1,
        string? City,
        string? State,
        string? ZipCode,
        string? Country)
    {
        public static ReverseGeocodingResult Empty => new(null, null, null, null, null);
        public bool HasData => !string.IsNullOrWhiteSpace(City) || !string.IsNullOrWhiteSpace(StreetAddress1);
    }
}

