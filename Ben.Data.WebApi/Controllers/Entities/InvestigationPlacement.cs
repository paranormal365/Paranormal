using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Works out where an investigation happened and writes that onto it.
/// </summary>
/// <remarks>
/// <para>Shared by the case-bound <see cref="InvestigationController"/> and the case-less
/// <see cref="OrgInvestigationsController"/>, because "where did this visit happen" has to have one
/// answer. Two copies would drift, and the first symptom would be a dot appearing on one map and
/// not another.</para>
///
/// <para>The <c>Latitude</c>, <c>Longitude</c>, <c>GeocodeNote</c> and <c>DateGeocoded</c> columns
/// on <see cref="Investigation"/> have existed since the <c>AddInvestigationCoordinates</c>
/// migration and <b>nothing has ever written them</b>. This is what finally does.</para>
/// </remarks>
internal static class InvestigationPlacement
{
    /// <summary>
    /// Resolves the place for an investigation and stamps its coordinates.
    /// </summary>
    /// <param name="db">The caller's context. Any new place is added but not saved.</param>
    /// <param name="investigation">Mutated in place with the resolved position.</param>
    /// <param name="placeId">An existing place chosen by the caller, if any.</param>
    /// <param name="newPlace">Inline details for a place being created as part of this request.</param>
    /// <param name="userId">Recorded as the creator of any new place.</param>
    /// <param name="ct">Cancellation for the lookup and any geocoding call.</param>
    /// <returns>
    /// The resolved place, and an error message to return as a 400 when it failed. The place comes
    /// back rather than being stashed anywhere, because callers need it to work out the default
    /// sharing scope and a static field on a shared helper would be a race between requests.
    /// </returns>
    internal static async Task<PlacementResult> ApplyAsync(
        BenDataContext db,
        Investigation investigation,
        Guid? placeId,
        NewPlaceRequest? newPlace,
        Guid userId,
        CancellationToken ct)
    {
        Place? place = null;

        if (placeId is { } id)
        {
            place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (place is null) return new PlacementResult(null, "That place could not be found.");
        }
        else if (newPlace is not null && newPlace.HasAnything)
        {
            place = new Place
            {
                Id = Guid.NewGuid(),
                Name = Trimmed(newPlace.Name),
                StreetAddress1 = Trimmed(newPlace.StreetAddress1),
                StreetAddress2 = Trimmed(newPlace.StreetAddress2),
                City = Trimmed(newPlace.City),
                State = Trimmed(newPlace.State),
                ZipCode = Trimmed(newPlace.ZipCode),
                Country = Trimmed(newPlace.Country) ?? "US",
                Latitude = newPlace.Latitude,
                Longitude = newPlace.Longitude,
                Kind = newPlace.Kind ?? PlaceKind.PrivateResidence,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            };
            await PlaceGeocoder.GeocodeAsync(place, trustSuppliedCoordinates: true, ct);
            db.Places.Add(place);
        }
        else if (investigation.CaseId is { } caseId)
        {
            // A case-bound investigation with no place of its own inherits the case's. The team
            // usually went to the address on file, and making them re-enter it would guarantee the
            // two drift apart.
            place = await db.Cases.AsNoTracking()
                .Where(c => c.Id == caseId && c.PlaceId != null)
                .Select(c => c.Place!)
                .FirstOrDefaultAsync(ct);
        }

        if (place is null)
        {
            // Not an error for a case-bound visit — it simply has no map position yet, and the
            // note says why rather than leaving a silent blank.
            investigation.GeocodeNote ??= "No location has been set for this investigation yet.";
            return new PlacementResult(null, null);
        }

        investigation.PlaceId = place.Id;
        investigation.Latitude = place.Latitude;
        investigation.Longitude = place.Longitude;
        // The place's own explanation carries across verbatim. Re-wording it here would give the
        // same failure two different descriptions depending on which screen you read it from.
        investigation.GeocodeNote = place.GeocodeNote;
        investigation.DateGeocoded = place.DateGeocoded;
        return new PlacementResult(place, null);
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Where an investigation ended up, or why it could not be placed.</summary>
/// <param name="Place">Null when nothing was resolved — not always an error, see ApplyAsync.</param>
/// <param name="Error">A message to return as a 400, or null on success.</param>
internal readonly record struct PlacementResult(Place? Place, string? Error);

/// <summary>
/// A place being created inline with the investigation that happens there.
/// </summary>
/// <remarks>
/// Exists so a group can schedule a visit to somewhere the system has never heard of without a
/// separate "create the place first" step — which, in practice, is how every landmark visit
/// starts. <see cref="PlaceKind"/> defaults to the cautious value when unstated.
/// </remarks>
public sealed record NewPlaceRequest(
    string? Name,
    string? StreetAddress1,
    string? StreetAddress2,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    decimal? Latitude = null,
    decimal? Longitude = null,
    PlaceKind? Kind = null)
{
    /// <summary>Whether the caller supplied anything at all, rather than an empty shell.</summary>
    internal bool HasAnything =>
        !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(StreetAddress1)
        || !string.IsNullOrWhiteSpace(City)
        || (Latitude.HasValue && Longitude.HasValue);
}
