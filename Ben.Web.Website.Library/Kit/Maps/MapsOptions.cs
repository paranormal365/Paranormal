namespace Ben.Web.Website.Library.Kit.Maps;

/// <summary>
/// What every map component needs to know at startup: whether the website can sign a MapKit
/// token. A map is Apple's or it is a notice that the map could not be loaded; there is no
/// second library behind it (item 228 retired the Telerik map over OpenStreetMap tiles).
/// </summary>
public sealed record MapsOptions(bool Configured)
{
    /// <summary>The path MapKit JS fetches its token from, same origin as the page.</summary>
    public const string TokenPath = "/auth/mapkit-token";
}
