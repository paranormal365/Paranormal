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
/// <param name="IsPublicPlace">
/// True when the visit is at a landmark anyone can go to, false when it is somewhere a person
/// lives, and <b>null when the caller does not know</b> — which is not the same as false.
/// </param>
/// <remarks>
/// <para>The three-state <see cref="IsPublicPlace"/> is deliberate. Only a known landmark is drawn
/// differently; an unknown one keeps exactly the marker it had. Defaulting an unknown to "private"
/// would be a map quietly asserting something about somebody's home that nobody checked.</para>
/// </remarks>
public sealed record InvestigationMapPin(
    Guid Id,
    string Title,
    decimal? Latitude,
    decimal? Longitude,
    bool IsPast,
    bool? IsPublicPlace = null);

/// <summary>The part of the world a map is showing, as its four edges and the zoom level.</summary>
public sealed record MapViewport(double North, double South, double East, double West, double Zoom);
