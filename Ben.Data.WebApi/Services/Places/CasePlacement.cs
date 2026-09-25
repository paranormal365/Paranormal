using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// Works out which place a case is about, and writes that onto it.
/// </summary>
/// <remarks>
/// <para><b>Why this did not exist before 2026-09-17.</b> <see cref="Case.PlaceId"/> has been on
/// the entity since the <c>BackfillPlacesFromCases</c> migration, and until now the only things
/// that ever wrote it were that backfill and the admin merge screen. Opening a case named no place
/// at all: a case got one only later and sideways, when somebody scheduled a visit and
/// <see cref="InvestigationPlacement"/> happened to bind one. So a case sat at an address that
/// matched a place three other groups had worked, and nothing connected the two.</para>
///
/// <para>Ben, 2026-09-17: "if someone wants to create a case at a public case, they are given the
/// option to link to the existing case and create their own investigation." The linking is this —
/// a case and a visit now point at the same shared place, so the place's page can gather them.</para>
///
/// <para><b>The case's own address is never rewritten.</b> That is deliberate and
/// <see cref="Case.PlaceId"/>'s own documentation says why: the address on a case is the record of
/// what the client actually reported, and the place is a shared row that another organization may
/// later correct. Only the coordinates are filled in, and only when the case has none, so a case
/// somebody typed without a position still appears on a map.</para>
///
/// <para><b>There is no fallback place.</b> A request that names neither an existing place nor a
/// new one leaves the case unplaced, exactly as every case before today. The plan called for
/// deriving one from the case's own address; that was dropped on contact with the question it
/// cannot answer — what kind of location is it? Guessing a residence would designate the case
/// private-lane work permanently and ask the paid-plan gate, over a question nobody was asked.
/// Guessing a public location could put somebody's home on a public page. So the kind is a
/// required choice on the New Case page instead, and an older caller that sends nothing is no
/// worse off than it was.</para>
/// </remarks>
internal static class CasePlacement
{
    /// <summary>
    /// Resolves the place for a case and binds it.
    /// </summary>
    /// <param name="db">The caller's context. Any new place is added but not saved.</param>
    /// <param name="theCase">Mutated in place: <c>PlaceId</c>, and coordinates when it had none.</param>
    /// <param name="placeId">An existing place chosen by the caller, if any.</param>
    /// <param name="newPlace">Inline details for a place being created with the case.</param>
    /// <param name="userId">Recorded as the creator of any new place.</param>
    /// <param name="ct">Cancellation for the lookup and any geocoding call.</param>
    /// <returns>
    /// The resolved place and an error to return as a 400. The place comes back because the caller
    /// needs its kind to decide how public the case is.
    /// </returns>
    internal static async Task<PlacementResult> ApplyAsync(
        BenDataContext db,
        Case theCase,
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
            place = await PlaceFactory.CreateAsync(newPlace, userId, ct);
            db.Places.Add(place);
        }

        // Nothing named, nothing derived. Not an error: the case simply has no shared place yet,
        // which is where every case stood until today.
        if (place is null) return new PlacementResult(null, null);

        // ── Item 184: binding a residence is the moment a case becomes private-lane work ──
        // The same rule InvestigationPlacement applies, asked here because this is the earlier
        // door: a case that names a home at birth is private-lane from birth rather than from
        // whenever somebody first schedules a visit. Never re-asked of an already-designated case
        // (grandfathering) — the plan governs new commitments, not work in hand.
        if (place.Kind == PlaceKind.PrivateResidence && !theCase.IsPrivateEngagement)
        {
            if (await PrivateCaseGate.RefusalAsync(db, theCase.OrganizationId, ct) is { } refusal)
                return new PlacementResult(null, refusal);

            theCase.IsPrivateEngagement = true;
        }

        theCase.PlaceId = place.Id;

        // Filled, never overwritten. A case carries the position of what was reported; the place's
        // is a shared answer, and helpful only where the case has none of its own.
        theCase.Latitude ??= place.Latitude;
        theCase.Longitude ??= place.Longitude;

        return new PlacementResult(place, null);
    }
}
