using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── Local discovery (backlog item #88) ───────────────────────────────────────
// What is near a point, in two lists that deliberately obey different privacy rules. See
// SearchController.Nearby for the reasoning: an organization that asked to be findable is a
// business listing and appears as precisely as it chose, while a public event is an invitation and
// stays approximate.

/// <summary>What is near a point.</summary>
public sealed record NearbyResults(
    IReadOnlyList<NearbyOrgResult> Organizations,
    IReadOnlyList<NearbyEventResult> Events,
    /// <summary>
    /// Public locations near the caller that have something published at them.
    /// </summary>
    /// <remarks>
    /// Item #88's stated shape asked for "toggles for groups / events / places" and the places
    /// tier was never built; the 2026-09-17 audit found the consequence — the place hub is the
    /// public front door for a location and a stranger had no way to reach one. There is no
    /// /places index, /find searches groups only, and the nav has no Places entry, so the only
    /// routes in were a feed card, a public investigation page and a venue page.
    ///
    /// Defaults to empty so an older server simply shows no places rather than failing.
    /// </remarks>
    IReadOnlyList<NearbyPlaceResult>? Places = null);

/// <summary>
/// One public location near the caller, with a count of what has been published there.
/// </summary>
/// <remarks>
/// <para><b>Exact coordinates, deliberately.</b> A public location is a landmark, a business or
/// somewhere that runs tours — the same position as an organization that opted into search, and
/// the opposite of a case or a residence. Grid-snapping a cave would break the feature rather
/// than protect anybody.</para>
///
/// <para><b>Only places with something to read.</b> A place row is created by the first group to
/// work there, so listing every one of them would hand a stranger a directory of addresses
/// somebody once typed. A place earns its way into this list by having published work at it.</para>
///
/// <para>Private residences can never appear here, whatever they have at them.</para>
/// </remarks>
public sealed record NearbyPlaceResult(
    Guid     PlaceId,
    string?  Name,
    string?  City,
    string?  State,
    decimal? Latitude,
    decimal? Longitude,
    double   DistanceMiles,
    int      InvestigationCount,
    int      SessionCount);

/// <summary>
/// One public event near the caller.
/// </summary>
/// <remarks>
/// <see cref="Latitude"/> and <see cref="Longitude"/> are the centre of a grid cell several miles
/// across, and <see cref="DistanceMiles"/> is measured to that same point — there is no field here
/// carrying anything more precise, and an exact address deliberately has nowhere to live.
/// </remarks>
public sealed record NearbyEventResult(
    Guid     EventId,
    string   Title,
    string?  UrlName,
    string   OrgName,
    string?  OrgUrlName,
    DateTime StartDateTime,
    string?  City,
    string?  State,
    decimal? Latitude,
    decimal? Longitude,
    double   DistanceMiles,
    /// <summary>
    /// The IANA zone the night happens in, when it is recorded — today, the tour's. Null means
    /// nobody has said, and the reader is shown UTC and told so; see EventClock.
    /// </summary>
    string?  TimeZoneId = null);

/// <summary>
/// One organization near the caller, at whatever precision it chose when it opted into search.
/// </summary>
public sealed record NearbyOrgResult(
    Guid     OrgId,
    string   OrgName,
    string   OrgUrlName,
    double   DistanceMiles,
    OrganizationAddressVisibility  Visibility,
    OrganizationAddressDisplayMode PublicDisplayMode,
    decimal? Latitude,
    decimal? Longitude,
    double?  RegionRadiusMiles,
    string?  StreetAddress1,
    string?  City,
    string?  State);
