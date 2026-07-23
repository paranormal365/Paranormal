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

    private sealed class NominatimResult
    {
        public string Lat { get; set; } = string.Empty;
        public string Lon { get; set; } = string.Empty;
        public string? Type { get; set; }
    }
}
