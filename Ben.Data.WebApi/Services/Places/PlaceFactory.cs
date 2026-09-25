using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// The one way a <see cref="Place"/> is built from what somebody typed.
/// </summary>
/// <remarks>
/// <para>Extracted from <see cref="InvestigationPlacement"/> when cases learned to name a place
/// too (2026-09-17). Three doors now create places from an inline description — a case, a
/// case-bound visit and a case-less visit — and the trimming, the country default, the cautious
/// <see cref="PlaceKind"/> default and the geocoding call have to be identical at all three. A
/// second copy would drift, and the way anybody would find out is a place that maps from one
/// screen and not another, or a home quietly recorded as a public location.</para>
///
/// <para><b>The kind default is the cautious one on purpose.</b> A description that does not say
/// what kind of location it is becomes a <see cref="PlaceKind.PrivateResidence"/>, because that is
/// the value whose consequences are all withholding: findings stay with the group, publication is
/// refused, and the paid-lane gate is asked. Getting that wrong in the other direction would put
/// somebody's home on a public page. Callers that know better say so.</para>
/// </remarks>
internal static class PlaceFactory
{
    /// <summary>
    /// Builds a place from an inline description and resolves its coordinates.
    /// </summary>
    /// <param name="request">What somebody typed. Not checked for emptiness — see
    /// <see cref="NewPlaceRequest.HasAnything"/>, which the callers ask first.</param>
    /// <param name="userId">Recorded as the creator.</param>
    /// <param name="ct">Cancellation for the geocoding call.</param>
    /// <returns>An unsaved place. The caller owns adding it and the transaction.</returns>
    internal static async Task<Place> CreateAsync(
        NewPlaceRequest request, Guid userId, CancellationToken ct)
    {
        var place = new Place
        {
            Id = Guid.NewGuid(),
            Name = Trimmed(request.Name),
            StreetAddress1 = Trimmed(request.StreetAddress1),
            StreetAddress2 = Trimmed(request.StreetAddress2),
            City = Trimmed(request.City),
            State = Trimmed(request.State),
            ZipCode = Trimmed(request.ZipCode),
            Country = Trimmed(request.Country) ?? "US",
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Kind = request.Kind ?? PlaceKind.PrivateResidence,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        // Supplied coordinates are trusted: the browser resolved them from the map preview
        // somebody is already looking at, and re-querying would spend an Apple Maps request to
        // re-derive the same answer.
        await PlaceGeocoder.GeocodeAsync(place, trustSuppliedCoordinates: true, ct);
        return place;
    }

    internal static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
