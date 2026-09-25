using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Controllers.Public;

namespace Ben.Data.WebApi.Services.Places;

/// <summary>
/// The one place that decides how much of a <see cref="Place"/> may be told to a given caller.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A place row is the same shape whether it is the Bell Witch Cave or
/// a client's house, and until this existed both endpoints that serve one — the anonymous
/// <see cref="PublicPlaceController"/> and the signed-in <see cref="PlaceController"/> — returned
/// the identical projection: street address, ZIP, and exact coordinates. Found in the 2026-09-17
/// audit. The anonymous one is the sharper half, because the place page became a signed-out
/// destination in the public-place-hub arc while the projection dated from 2026-08-15 and had never
/// been through either <see cref="PublicCoordinates"/> or the redaction work.</para>
///
/// <para><b>A residence's address is the whole disclosure.</b> Every other rule already refuses to
/// publish anything about a private residence: an investigation there can never be
/// <see cref="InvestigationVisibility.Public"/>, a feed post about one is refused, an event at one
/// is refused, publishing a field session to one is refused. So the public shape of a residence is
/// empty lists plus the address — and the address is the only thing it must not carry.</para>
///
/// <para><b>The name goes too, not just the street.</b> A residence is routinely named after the
/// family living in it, which is why every prose surface redacts a place's name through the case
/// roster. This withholds it outright instead: the place page has no single case whose roster would
/// apply, and a union of rosters would be a second rule to keep in step with the first.</para>
///
/// <para><b>City and state stay.</b> They are what a public case already publishes about its own
/// location, so withholding them here would take away a fact the rest of the site discloses on
/// purpose, and would leave the place page unable to say anything at all.</para>
///
/// <para><b>Signing in is not standing.</b> Accounts are free and self-service, so "any signed-in
/// caller" is the same audience as "anybody" with one extra step. The signed-in endpoint therefore
/// asks for a reason to know: the caller's group has worked there, or they created the row, or they
/// are a SuperAdmin. Everyone else gets the public shape — which is the shape they would get by
/// signing out, so nothing is hidden from them that they could have had anyway.</para>
/// </remarks>
internal static class PlaceDisclosure
{
    /// <summary>
    /// The place as anybody may see it: full detail for a public location, and for a residence the
    /// city, the state and an approximated position.
    /// </summary>
    internal static PlaceRecord Public(
        Guid id, string? name, string? streetAddress1, string? city, string? state,
        string? zipCode, string? country, decimal? latitude, decimal? longitude,
        string? geocodeNote, PlaceKind kind, string? description = null)
    {
        if (kind != PlaceKind.PrivateResidence)
            return new PlaceRecord(
                id, name, streetAddress1, city, state, zipCode, country,
                latitude, longitude, geocodeNote, kind, description);

        // Snapped to a grid rather than dropped, so the page can still say roughly where this is
        // without the pin being an address lookup. PublicCoordinates owns how coarse that is.
        var (approxLat, approxLon) = PublicCoordinates.Approximate(latitude, longitude);

        return new PlaceRecord(
            Id: id,
            // Withheld, not redacted — see the class remarks.
            Name: null,
            StreetAddress1: null,
            City: city,
            State: state,
            // A ZIP is a small enough area to be an address in a rural county.
            ZipCode: null,
            Country: country,
            Latitude: approxLat,
            Longitude: approxLon,
            // The geocoder's own note quotes back what it matched, which is the address.
            GeocodeNote: null,
            Kind: kind,
            // Withheld with the rest of it. A description of somebody's home is a description of
            // somebody's home however carefully it was written.
            Description: null);
    }

    /// <summary>
    /// Whether this caller has a reason to know a residence's address: one of their groups has
    /// worked there, they created the row, or they administer the site.
    /// </summary>
    /// <param name="isSuperAdmin">Resolved by the caller — SuperAdmin is a role on the principal,
    /// not a column, so this helper cannot ask the database for it.</param>
    internal static async Task<bool> MaySeeInFullAsync(
        BenDataContext db, Guid placeId, Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        if (isSuperAdmin) return true;
        if (userId == Guid.Empty) return false;

        if (await db.Places.AsNoTracking()
                .AnyAsync(p => p.Id == placeId && p.CreatedByAppUserId == userId, ct))
            return true;

        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        if (myOrgIds.Count == 0) return false;

        // Worked there means either a case about the place or a visit to it. Either one is a group
        // that was given the address by the person who lives there.
        if (await db.Cases.AsNoTracking()
                .AnyAsync(c => c.PlaceId == placeId && myOrgIds.Contains(c.OrganizationId), ct))
            return true;

        return await db.Investigations.AsNoTracking()
            .AnyAsync(i => i.PlaceId == placeId && myOrgIds.Contains(i.OrganizationId), ct);
    }
}
