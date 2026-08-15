using Ben.Data.Source.Entities;
using static Ben.Service.RepositoryService.Services.AddressGeocodingService;

namespace Ben.Service.RepositoryService.Services;

/// <summary>
/// Resolves a <see cref="Place"/>'s coordinates, and records why when it cannot.
/// </summary>
/// <remarks>
/// <para>The rule this exists to enforce: a place that fails to geocode gets a
/// <see cref="Place.GeocodeNote"/> saying so, never a silent pair of nulls. A missing dot on a map
/// is otherwise indistinguishable from a place nobody has visited yet, and whoever could fix the
/// address has no way of knowing there is anything to fix.</para>
///
/// <para>Split into an async lookup and a pure <see cref="Apply"/> deliberately. Every decision
/// this class makes — when to skip, what note to write, whether to clear a stale note — lives in
/// the pure half, so it can be tested exhaustively without a network call and without a fake
/// geocoder that would only prove the fake works.</para>
/// </remarks>
public static class PlaceGeocoder
{
    /// <summary>
    /// Fills in the place's coordinates from its address, or explains the gap.
    /// </summary>
    /// <param name="place">Mutated in place. Not saved — the caller owns the transaction.</param>
    /// <param name="trustSuppliedCoordinates">
    /// When true and the place already carries coordinates, no lookup happens. Set this when the
    /// client resolved them from the live map preview: re-querying would spend a request to
    /// re-derive an answer somebody is already looking at.
    /// </param>
    public static async Task GeocodeAsync(
        Place place, bool trustSuppliedCoordinates = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (trustSuppliedCoordinates && place.Latitude.HasValue && place.Longitude.HasValue)
        {
            // Nothing was looked up, but the place does have coordinates, so any note explaining
            // their absence is now false. Clearing it is part of the same honesty rule.
            place.GeocodeNote = null;
            place.DateGeocoded ??= DateTime.UtcNow;
            return;
        }

        var result = HasStructuredAddress(place)
            ? await TryResolveCoordinatesAsync(
                place.StreetAddress1!, place.StreetAddress2,
                place.City!, place.State!, place.ZipCode!,
                string.IsNullOrWhiteSpace(place.Country) ? "US" : place.Country!, ct)
            // A landmark may be a name and nothing else — "The Bell Witch Cave" is a better query
            // than a blank address, and refusing to try would leave every named place unmapped.
            : await TryResolveFromQueryAsync(FreeTextQuery(place), ct);

        Apply(place, result);
    }

    /// <summary>
    /// Writes a lookup result onto a place: coordinates and a timestamp when it worked, an
    /// explanatory note when it did not. Pure, so the whole decision table is testable.
    /// </summary>
    public static void Apply(Place place, GeocodingLookupResult result)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Latitude.HasValue && result.Longitude.HasValue)
        {
            place.Latitude = result.Latitude;
            place.Longitude = result.Longitude;
            place.DateGeocoded = DateTime.UtcNow;
            place.GeocodeNote = null;
            return;
        }

        // Deliberately does not blank existing coordinates. A lookup that fails today says nothing
        // about one that succeeded last week — throwing away a good answer because a later attempt
        // timed out would lose data to a transient failure.
        place.GeocodeNote = NoteFor(place);
        if (!place.Latitude.HasValue || !place.Longitude.HasValue) place.DateGeocoded = null;
    }

    /// <summary>Whether there is a full street address to hand the structured lookup.</summary>
    private static bool HasStructuredAddress(Place place)
        => !string.IsNullOrWhiteSpace(place.StreetAddress1)
        && !string.IsNullOrWhiteSpace(place.City)
        && !string.IsNullOrWhiteSpace(place.State)
        && !string.IsNullOrWhiteSpace(place.ZipCode);

    /// <summary>Everything known about the place, joined, for the free-text lookup.</summary>
    private static string FreeTextQuery(Place place)
        => string.Join(", ", new[]
            {
                place.Name, place.StreetAddress1, place.City, place.State, place.ZipCode,
            }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim()));

    /// <summary>
    /// Why this place has no coordinates, in terms the person who typed the address can act on.
    /// </summary>
    private static string NoteFor(Place place)
    {
        if (!HasStructuredAddress(place) && string.IsNullOrWhiteSpace(FreeTextQuery(place)))
            return "No address or name to look up. Add a street address, or a name for the location.";

        return HasStructuredAddress(place)
            ? "The address could not be found on the map. Check it for typos, or set the location by hand."
            : "There is not enough of an address to place this on the map. Add a street address, city, state and ZIP.";
    }
}
