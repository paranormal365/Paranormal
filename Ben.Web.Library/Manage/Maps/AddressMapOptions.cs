using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Service.Models.Entities;
using Telerik.SvgIcons;

namespace Ben.Web.Library.Manage.Maps;

// ── Bindable config model (form + map state) ─────────────────────────────────

/// <summary>
/// All map display settings for a single address.
/// Used as the bindable form model in <see cref="AddressMapPlayer"/>.
/// </summary>
public record AddressMapConfig
{
    public bool IsOnMap { get; init; } = false;
    public bool ShowMarker { get; init; } = true;
    public bool ShowRegion { get; init; } = false;
    public double RegionRadiusMiles { get; init; } = 1.0;
    public string MarkerColor { get; init; } = "#e63535";
    public string? MarkerIconKey { get; init; }
    public string RegionFillColor { get; init; } = "#3388ff";
    public double RegionFillOpacity { get; init; } = 0.2;
    public string RegionStrokeColor { get; init; } = "#1155cc";
    public double RegionStrokeOpacity { get; init; } = 0.8;
    public double RegionStrokeWidth { get; init; } = 2.0;

    public static AddressMapConfig Default => new();

    public static AddressMapConfig FromRecord(AddressMapConfigRecord? r) => r is null ? Default : new()
    {
        IsOnMap            = r.IsOnMap,
        ShowMarker         = r.ShowMarker,
        ShowRegion         = r.ShowRegion,
        RegionRadiusMiles  = r.RegionRadiusMiles,
        MarkerColor        = r.MarkerColor,
        MarkerIconKey      = r.MarkerIconKey,
        RegionFillColor    = r.RegionFillColor,
        RegionFillOpacity  = r.RegionFillOpacity,
        RegionStrokeColor  = r.RegionStrokeColor,
        RegionStrokeOpacity = r.RegionStrokeOpacity,
        RegionStrokeWidth  = r.RegionStrokeWidth,
    };
}

// ── Icon registry ─────────────────────────────────────────────────────────────

/// <summary>An icon option for the map marker icon picker.</summary>
public record MapMarkerIconOption(string Key, string Label, ISvgIcon Icon, string SvgPath);

/// <summary>
/// Curated set of Telerik/Kendo SVG icons suitable for map markers.
/// The <see cref="SvgPath"/> is the raw SVG path data used in the JS marker template.
/// </summary>
public static class AddressMapIconRegistry
{
    // SVG path data (512×512 viewBox) for each icon
    // map-marker-target path sourced from Telerik docs example
    public const string PathMapMarkerTarget =
        "M256 0C158.8 0 80 78.8 80 176s176 336 176 336 176-238.8 176-336S353.2 0 256 0" +
        "m0 288c-61.9 0-112-50.1-112-112S194.1 64 256 64s112 50.1 112 112-50.1 112-112 112" +
        "m48-112c0 26.5-21.5 48-48 48s-48-21.5-48-48 21.5-48 48-48 48 21.5 48 48";

    public const string PathPin =
        "M256 0C167.6 0 96 71.6 96 160c0 120 160 352 160 352s160-232 160-352C416 71.6 344.4 0 256 0z" +
        "M256 224c-35.3 0-64-28.7-64-64s28.7-64 64-64 64 28.7 64 64-28.7 64-64 64z";

    public const string PathHome =
        "M501.5 232.5l-240-224a16 16 0 0 0-21.9 0l-240 224A16 16 0 0 0 10.5 256H64v240a16 16 0 0 0 16 16h128V352h96v160h128a16 16 0 0 0 16-16V256h53.5a16 16 0 0 0 10.9-23.5z";

    public const string PathBuilding =
        "M0 48v416h160V352h192v112h160V48H0zM128 288H64v-64h64v64zm0-128H64v-64h64v64z" +
        "M256 288h-64v-64h64v64zm0-128h-64v-64h64v64zm128 128h-64v-64h64v64zm0-128h-64v-64h64v64z";

    public const string PathStar =
        "M512 196.8l-176-25.6L256 16l-80 155.2L0 196.8l128 124.8-30.4 176L256 416l158.4 81.6L384 321.6z";

    public const string PathHeart =
        "M462 62.7C422.4 23.1 372.7 0 316.9 0c-33.8 0-66 8.6-94.7 24.8L256 45.6l33.8-20.8" +
        "C317.5 8.7 349.8 0 382.1 0c55.8 0 105.5 23.1 145.1 62.7 82.3 82.3 82.3 215.9 0 298.2" +
        "L256 512 0 360.9c-82.3-82.3-82.3-215.9 0-298.2z";

    public const string PathFlag =
        "M96 64v384h32V256h288l-64-96 64-96H96z";

    public const string PathCamera =
        "M416 128H304l-48-64H160L112 128H48C21.5 128 0 149.5 0 176v240c0 26.5 21.5 48 48 48h368" +
        "c26.5 0 48-21.5 48-48V176c0-26.5-21.5-48-48-48zM256 384c-53 0-96-43-96-96s43-96 96-96" +
        "s96 43 96 96-43 96-96 96zm0-160c-35.3 0-64 28.7-64 64s28.7 64 64 64 64-28.7 64-64-28.7-64-64-64z";

    public static readonly IReadOnlyList<MapMarkerIconOption> All = new List<MapMarkerIconOption>
    {
        new("map-marker-target", "Map Pin (default)", SvgIcon.MapMarkerTarget, PathMapMarkerTarget),
        new("pin",               "Pin",               SvgIcon.Pin,             PathPin),
        new("pin-solid",         "Pin Solid",         SvgIcon.PinSolid,        PathPin),
        new("home",              "Home",              SvgIcon.Home,            PathHome),
        new("globe",             "Globe",             SvgIcon.Globe,           PathBuilding),
        new("star",              "Star",              SvgIcon.Star,            PathStar),
        new("heart",             "Heart",             SvgIcon.Heart,           PathHeart),
        new("camera",            "Camera",            SvgIcon.Camera,          PathCamera),
    };

    public static string GetPath(string? key) =>
        All.FirstOrDefault(i => i.Key == key)?.SvgPath ?? PathMapMarkerTarget;
}

// ── GeoJSON helper ────────────────────────────────────────────────────────────

/// <summary>Helpers for computing geographic shapes as GeoJSON objects.</summary>
public static class MapGeoJsonHelper
{
    /// <summary>
    /// Computes a circle approximation as a GeoJSON Polygon FeatureCollection.
    /// Pass the result directly to the Telerik Map Shape layer <c>Data</c> parameter.
    /// </summary>
    /// <param name="lat">Center latitude in decimal degrees.</param>
    /// <param name="lon">Center longitude in decimal degrees.</param>
    /// <param name="radiusMiles">Radius in statute miles.</param>
    /// <param name="segments">Number of polygon vertices (default 64).</param>
    public static object ComputeCircleGeoJson(
        double lat, double lon, double radiusMiles, int segments = 64)
    {
        var ring = new List<double[]>(segments + 1);
        double latRad = lat * Math.PI / 180.0;

        for (int i = 0; i <= segments; i++)  // <= to close the ring
        {
            double angle = 2.0 * Math.PI * i / segments;
            double dLat  = (radiusMiles / 69.0) * Math.Cos(angle);
            double dLon  = (radiusMiles / (69.0 * Math.Cos(latRad))) * Math.Sin(angle);
            ring.Add([lon + dLon, lat + dLat]);  // GeoJSON is [longitude, latitude]
        }

        return new
        {
            type = "FeatureCollection",
            features = new[]
            {
                new
                {
                    type = "Feature",
                    geometry = new
                    {
                        type = "Polygon",
                        coordinates = new[] { ring }
                    },
                    properties = new { }
                }
            }
        };
    }

    /// <summary>
    /// Computes the great-circle distance in statute miles between two lat/lng points
    /// using the Haversine formula.
    /// </summary>
    public static double HaversineDistanceMiles(
        double lat1, double lon1, double lat2, double lon2)
    {
        const double R     = 3958.8;   // Earth radius in miles
        const double toRad = Math.PI / 180.0;
        double dLat = (lat2 - lat1) * toRad;
        double dLon = (lon2 - lon1) * toRad;
        double a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(lat1 * toRad) * Math.Cos(lat2 * toRad) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    /// <summary>Returns a reasonable initial zoom level for a given radius in miles.</summary>
    public static double ZoomForRadius(double radiusMiles) => radiusMiles switch
    {
        <= 0.25 => 15,
        <= 0.5  => 14,
        <= 1    => 13,
        <= 2    => 12,
        <= 5    => 11,
        <= 10   => 10,
        <= 25   => 9,
        <= 50   => 8,
        _       => 7,
    };
}

// ── Marker data model for the Telerik Map layer ───────────────────────────────

/// <summary>Data model passed to the Telerik Map Marker layer.</summary>
public class AddressMarkerModel
{
    public double[]? LatLng { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>CSS color for the marker template function.</summary>
    public string Color { get; set; } = "#e63535";
    /// <summary>SVG path data string for the selected icon.</summary>
    public string IconSvgPath { get; set; } = AddressMapIconRegistry.PathMapMarkerTarget;
}
