namespace Ben.Web.Website.Library.Kit.Maps;

/// <summary>One thing on a map, as every map on the site describes it.</summary>
/// <param name="Latitude">Decimal degrees.</param>
/// <param name="Longitude">Decimal degrees.</param>
/// <param name="Title">Shown when the pin is selected. Never HTML.</param>
/// <param name="Subtitle">A second line under the title, if there is one.</param>
/// <param name="Glyph">A short mark drawn in the pin — an emoji or a number. Null for Apple's plain pin.</param>
/// <param name="Color">A CSS colour for the pin body. Null for the theme's default.</param>
/// <param name="Dimmed">Drawn at reduced opacity: ordinary, just no longer upcoming.</param>
/// <param name="Cluster">
/// Pins sharing a cluster key are gathered when they overlap, with the count drawn in their place.
/// Null keeps a pin out of clustering altogether.
/// </param>
public sealed record BenMapPin(
    double Latitude,
    double Longitude,
    string Title,
    string? Subtitle = null,
    string? Glyph = null,
    string? Color = null,
    bool Dimmed = false,
    string? Cluster = null);

/// <summary>The part of the world a map is showing, as its four edges and the zoom level.</summary>
public sealed record BenMapViewport(double North, double South, double East, double West, double Zoom);
