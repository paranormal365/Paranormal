namespace Ben.Service.RepositoryService.Services;

/// <summary>
/// The geocoding the address forms, the place records and the search box use: one static door,
/// configured once at startup, so the eight callers never learned which provider stood behind
/// it. Since item 230 (2026-09-10) that is the Apple Maps Server API through
/// <see cref="AppleMapsGeocoder"/>; before that, Geocodio, which was metered.
/// </summary>
/// <remarks>
/// Unconfigured — no Maps key — every lookup answers <see cref="GeocodingLookupResult.Empty"/>
/// or <see cref="ReverseGeocodingResult.Empty"/>, the same silent nothing the previous provider
/// gave without its key. The callers already explain that absence to the person.
/// </remarks>
public static class AddressGeocodingService
{
    private static AppleMapsGeocoder? _geocoder;

    /// <summary>Installs the provider. Null means "not configured": nothing is looked up.</summary>
    public static void Configure(AppleMapsGeocoder? geocoder) => _geocoder = geocoder;

    public static bool IsConfigured => _geocoder is not null;

    // ── Forward geocoding ─────────────────────────────────────────────────────

    public static Task<GeocodingLookupResult> TryResolveCoordinatesAsync(
        string streetAddress1,
        string? streetAddress2,
        string city,
        string state,
        string zipCode,
        string country,
        CancellationToken cancellationToken = default)
        => _geocoder is null
            ? Task.FromResult(GeocodingLookupResult.Empty)
            : _geocoder.ResolveAsync(streetAddress1, streetAddress2, city, state, zipCode, country, cancellationToken);

    /// <summary>
    /// Geocodes a freeform query (e.g. "705 Meeting St, Franklin, TN 37064", or just a town).
    /// Does not require individual address components.
    /// </summary>
    public static Task<GeocodingLookupResult> TryResolveFromQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
        => _geocoder is null
            ? Task.FromResult(GeocodingLookupResult.Empty)
            : _geocoder.ResolveFromQueryAsync(query, cancellationToken);

    public static Task<ReverseGeocodingResult> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
        => _geocoder is null
            ? Task.FromResult(ReverseGeocodingResult.Empty)
            : _geocoder.ReverseAsync(latitude, longitude, cancellationToken);

    // ── Result records ────────────────────────────────────────────────────────

    /// <param name="ResultType">How precise the hit is: <c>rooftop</c>, <c>street</c> or <c>place</c>.</param>
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
