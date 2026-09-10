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
/// <param name="IconSvgPath">
/// An SVG path (512×512 viewBox) drawn in the pin instead of <paramref name="Glyph"/> — the icon
/// a person chose for their address marker. Null means glyph or plain pin.
/// </param>
public sealed record BenMapPin(
    double Latitude,
    double Longitude,
    string Title,
    string? Subtitle = null,
    string? Glyph = null,
    string? Color = null,
    bool Dimmed = false,
    string? Cluster = null,
    string? IconSvgPath = null);

/// <summary>A filled circle of a real radius on the ground — an address's region.</summary>
public sealed record BenMapCircle(
    double Latitude,
    double Longitude,
    double RadiusMeters,
    string FillColor,
    double FillOpacity,
    string StrokeColor,
    double StrokeOpacity,
    double StrokeWidth);

/// <summary>Where on the ground somebody clicked.</summary>
public sealed record BenMapPoint(double Latitude, double Longitude);

/// <summary>The part of the world a map is showing, as its four edges and the zoom level.</summary>
public sealed record BenMapViewport(double North, double South, double East, double West, double Zoom);
