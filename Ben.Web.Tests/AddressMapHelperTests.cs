using Ben.Web.Library.Manage.Maps;
using Ben.Service.Models.Entities;
using Xunit;

namespace Ben.Web.Tests;

/// <summary>
/// Tests for the AddressMapPlayer helper classes:
/// MapGeoJsonHelper geographic calculations, AddressMapConfig record,
/// and AddressMapIconRegistry.
/// </summary>
public class AddressMapHelperTests
{
    // ── MapGeoJsonHelper ──────────────────────────────────────────────────────

    [Fact]
    public void GeoJsonHelper_ComputeCircle_ReturnsFeatureCollection()
    {
        var geojson = MapGeoJsonHelper.ComputeCircleGeoJson(30.2672, -97.7431, 1.0);
        Assert.NotNull(geojson);
        var type = geojson.GetType().GetProperty("type")?.GetValue(geojson) as string;
        Assert.Equal("FeatureCollection", type);
    }

    [Fact]
    public void GeoJsonHelper_ComputeCircle_ContainsFeatures()
    {
        var geojson = MapGeoJsonHelper.ComputeCircleGeoJson(30.2672, -97.7431, 1.0);
        var features = geojson.GetType().GetProperty("features")?.GetValue(geojson);
        Assert.NotNull(features);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(0.25)]
    public void GeoJsonHelper_ComputeCircle_DifferentRadii_NoException(double miles)
    {
        var ex = Record.Exception(() => MapGeoJsonHelper.ComputeCircleGeoJson(40.7128, -74.0060, miles));
        Assert.Null(ex);
    }

    [Fact]
    public void GeoJsonHelper_Haversine_AustinToSanAntonio()
    {
        // Straight-line (haversine) ~73-75 miles between city centers
        double dist = MapGeoJsonHelper.HaversineDistanceMiles(
            30.2672, -97.7431,
            29.4241, -98.4936);
        Assert.InRange(dist, 70.0, 80.0);
    }

    [Fact]
    public void GeoJsonHelper_Haversine_SamePoint_IsZero()
    {
        double dist = MapGeoJsonHelper.HaversineDistanceMiles(40.0, -74.0, 40.0, -74.0);
        Assert.Equal(0.0, dist, 6);
    }

    [Fact]
    public void GeoJsonHelper_Haversine_Symmetric()
    {
        double d1 = MapGeoJsonHelper.HaversineDistanceMiles(30.0, -97.0, 29.0, -96.0);
        double d2 = MapGeoJsonHelper.HaversineDistanceMiles(29.0, -96.0, 30.0, -97.0);
        Assert.Equal(d1, d2, 6);
    }

    [Theory]
    [InlineData(0.1, 15)]
    [InlineData(0.25, 15)]   // 0.25 <= 0.25 hits the first case
    [InlineData(1.0, 13)]
    [InlineData(5.0, 11)]
    [InlineData(10.0, 10)]
    [InlineData(50.0, 8)]
    public void GeoJsonHelper_ZoomForRadius_ExpectedLevels(double miles, double expected)
    {
        Assert.Equal(expected, MapGeoJsonHelper.ZoomForRadius(miles));
    }

    [Fact]
    public void GeoJsonHelper_ZoomForRadius_VeryLargeRadius()
    {
        double zoom = MapGeoJsonHelper.ZoomForRadius(200);
        Assert.Equal(7, zoom);   // falls through to default
    }

    // ── AddressMapConfig record ───────────────────────────────────────────────

    [Fact]
    public void AddressMapConfig_Default_HasExpectedValues()
    {
        var cfg = AddressMapConfig.Default;
        Assert.False(cfg.IsOnMap);
        Assert.True(cfg.ShowMarker);
        Assert.False(cfg.ShowRegion);
        Assert.Equal(1.0, cfg.RegionRadiusMiles);
        Assert.Equal("#e63535", cfg.MarkerColor);
        Assert.Null(cfg.MarkerIconKey);
    }

    [Fact]
    public void AddressMapConfig_FromRecord_Null_ReturnsDefault()
    {
        var cfg = AddressMapConfig.FromRecord(null);
        Assert.Equal(AddressMapConfig.Default, cfg);
    }

    [Fact]
    public void AddressMapConfig_FromRecord_MapsAllFields()
    {
        var record = new AddressMapConfigRecord
        {
            Id = Guid.NewGuid(), OrganizationAddressId = Guid.NewGuid(),
            IsOnMap = true, ShowMarker = false, ShowRegion = true,
            RegionRadiusMiles = 3.5, MarkerColor = "#0000ff",
            MarkerIconKey = "star", RegionFillColor = "#00ff00",
            RegionFillOpacity = 0.3, RegionStrokeColor = "#ff0000",
            RegionStrokeOpacity = 0.7, RegionStrokeWidth = 3.0,
        };
        var cfg = AddressMapConfig.FromRecord(record);
        Assert.True(cfg.IsOnMap);
        Assert.False(cfg.ShowMarker);
        Assert.True(cfg.ShowRegion);
        Assert.Equal(3.5, cfg.RegionRadiusMiles);
        Assert.Equal("star", cfg.MarkerIconKey);
        Assert.Equal("#0000ff", cfg.MarkerColor);
        Assert.Equal(0.3, cfg.RegionFillOpacity);
    }

    [Fact]
    public void AddressMapConfig_WithExpression_IsImmutable()
    {
        var original = AddressMapConfig.Default;
        var updated  = original with { RegionRadiusMiles = 5.0, IsOnMap = true };
        Assert.Equal(1.0, original.RegionRadiusMiles);  // original unchanged
        Assert.Equal(5.0, updated.RegionRadiusMiles);
        Assert.True(updated.IsOnMap);
    }

    // ── AddressMapIconRegistry ────────────────────────────────────────────────

    [Fact]
    public void IconRegistry_AllIconsHaveNonEmptyPaths()
    {
        foreach (var icon in AddressMapIconRegistry.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(icon.SvgPath),
                $"Icon '{icon.Key}' has empty SVG path.");
            Assert.False(string.IsNullOrWhiteSpace(icon.Key));
            Assert.False(string.IsNullOrWhiteSpace(icon.Label));
            Assert.NotNull(icon.Icon);
        }
    }

    [Fact]
    public void IconRegistry_HasAtLeastSixIcons()
    {
        Assert.True(AddressMapIconRegistry.All.Count >= 6);
    }

    [Fact]
    public void IconRegistry_GetPath_NullKey_ReturnsDefault()
    {
        string path = AddressMapIconRegistry.GetPath(null);
        Assert.Equal(AddressMapIconRegistry.PathMapMarkerTarget, path);
    }

    [Fact]
    public void IconRegistry_GetPath_UnknownKey_ReturnsDefault()
    {
        string path = AddressMapIconRegistry.GetPath("does-not-exist");
        Assert.Equal(AddressMapIconRegistry.PathMapMarkerTarget, path);
    }

    [Theory]
    [InlineData("map-marker-target")]
    [InlineData("pin")]
    [InlineData("home")]
    [InlineData("star")]
    [InlineData("heart")]
    [InlineData("camera")]
    public void IconRegistry_GetPath_KnownKeys_ReturnsNonEmptyPath(string key)
    {
        string path = AddressMapIconRegistry.GetPath(key);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.NotEqual(string.Empty, path);
    }

    [Fact]
    public void IconRegistry_AllKeysAreUnique()
    {
        var keys = AddressMapIconRegistry.All.Select(i => i.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
