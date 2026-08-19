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
    IReadOnlyList<NearbyEventResult> Events);

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
    double   DistanceMiles);

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
