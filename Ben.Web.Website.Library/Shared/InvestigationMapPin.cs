namespace Ben.Web.Website.Library.Shared;

/// <summary>
/// One investigation as <see cref="InvestigationsMap"/> needs it.
/// </summary>
/// <remarks>
/// A small shape of its own rather than the map taking <c>OrgInvestigationRow</c> directly, so the
/// same component can plot a person's own visits and, later, a place's history without every
/// caller having to produce the organization view's record.
/// </remarks>
/// <param name="Id">Passed back when the pin is clicked.</param>
/// <param name="Title">Shown in the marker's tooltip.</param>
/// <param name="Latitude">Null when the location could not be resolved — the pin is skipped.</param>
/// <param name="Longitude">Null when the location could not be resolved — the pin is skipped.</param>
/// <param name="IsPast">Dims the marker. A finished visit is ordinary, just no longer upcoming.</param>
public sealed record InvestigationMapPin(
    Guid Id,
    string Title,
    decimal? Latitude,
    decimal? Longitude,
    bool IsPast);
