using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ben.Service.RepositoryService.Services;

public static class AddressGeocodingService
{
    private static readonly HttpClient HttpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BenApp/1.0 (local-dev-geocoding)");
        return client;
    }

    public static async Task<GeocodingLookupResult> TryResolveCoordinatesAsync(
        string streetAddress1,
        string? streetAddress2,
        string city,
        string state,
        string zipCode,
        string country,
        CancellationToken cancellationToken = default)
    {
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
                new[] { streetAddress1, streetAddress2, city, state, zipCode, country }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim()));

            var encodedQuery = UrlEncoder.Default.Encode(query);
            var url = $"https://nominatim.openstreetmap.org/search?q={encodedQuery}&format=jsonv2&limit=1";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return GeocodingLookupResult.Empty;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var results = JsonSerializer.Deserialize<List<NominatimResult>>(json);
            var first = results?.FirstOrDefault();

            if (first is null)
            {
                return new GeocodingLookupResult(null, null, json, null);
            }

            if (!decimal.TryParse(first.Lat, NumberStyles.Any, CultureInfo.InvariantCulture, out var latitude) ||
                !decimal.TryParse(first.Lon, NumberStyles.Any, CultureInfo.InvariantCulture, out var longitude))
            {
                return new GeocodingLookupResult(null, null, json, first.Type);
            }

            return new GeocodingLookupResult(latitude, longitude, json, first.Type);
        }
        catch
        {
            // Geocoding failures should not block address submission.
            return GeocodingLookupResult.Empty;
        }
    }

    public sealed record GeocodingLookupResult(
        decimal? Latitude,
        decimal? Longitude,
        string? RawResponseJson,
        string? ResultType)
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

    public static async Task<ReverseGeocodingResult> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lat = latitude.ToString("G", CultureInfo.InvariantCulture);
            var lon = longitude.ToString("G", CultureInfo.InvariantCulture);
            var url = $"https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=jsonv2";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return ReverseGeocodingResult.Empty;

            var json     = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc      = JsonDocument.Parse(json);
            var address  = doc.RootElement.TryGetProperty("address", out var addr) ? addr : (JsonElement?)null;
            if (address is null) return ReverseGeocodingResult.Empty;

            string? Get(params string[] keys)
            {
                foreach (var k in keys)
                    if (address.Value.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                return null;
            }

            var houseNumber = Get("house_number");
            var road        = Get("road");
            var street1     = houseNumber is not null && road is not null
                                  ? $"{houseNumber} {road}"
                                  : road ?? houseNumber;

            var city        = Get("city", "town", "village", "municipality", "county");
            var state       = Get("state");
            var zip         = Get("postcode");
            var countryCode = Get("country_code");
            var country     = countryCode?.ToUpperInvariant() ?? Get("country");

            return new ReverseGeocodingResult(street1, city, state, zip, country);
        }
        catch
        {
            return ReverseGeocodingResult.Empty;
        }
    }

    private sealed class NominatimResult
    {
        public string Lat { get; set; } = string.Empty;
        public string Lon { get; set; } = string.Empty;
        public string? Type { get; set; }
    }
}
