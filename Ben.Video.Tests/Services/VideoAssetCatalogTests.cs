using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Tests for the asset catalog model, provider interface contract, and sync result logic.
/// VideoAssetCatalogService integration tests are deferred to Phase 50 (require HttpClient mock).
/// </summary>
public sealed class VideoAssetCatalogTests
{
    // ── VideoAssetCatalogItem ──────────────────────────────────────────────────

    [Fact]
    public void CatalogItem_DefaultSettings_AllFlagsFalse()
    {
        var item = new VideoAssetCatalogItem();
        Assert.False(item.Settings.AllowRecolor);
        Assert.False(item.Settings.AllowResize);
        Assert.False(item.Settings.AllowMotion);
        Assert.False(item.Settings.AllowControlPoints);
        Assert.False(item.Settings.AllowEasing);
        Assert.False(item.Settings.AllowEffects);
        Assert.False(item.Settings.AllowOpacity);
    }

    [Fact]
    public void CatalogItem_WithRecord_LocalAvailabilityUpdatable()
    {
        var item = new VideoAssetCatalogItem { Id = "abc", IsLocalAvailable = false };
        var updated = item with { IsLocalAvailable = true };

        Assert.False(item.IsLocalAvailable);
        Assert.True(updated.IsLocalAvailable);
        Assert.Equal("abc", updated.Id);   // other fields unchanged
    }

    [Fact]
    public void CatalogItem_Tags_DefaultEmpty()
    {
        var item = new VideoAssetCatalogItem();
        Assert.Empty(item.Tags);
    }

    [Fact]
    public void CatalogItem_ControlPoints_NullForRasterByDefault()
    {
        var png = new VideoAssetCatalogItem { Format = VideoAssetFormat.Png };
        Assert.Null(png.ControlPoints);
    }

    [Fact]
    public void CatalogItem_ControlPoints_SupportedForSvg()
    {
        var svg = new VideoAssetCatalogItem
        {
            Format        = VideoAssetFormat.Svg,
            ControlPoints = new List<SvgControlPoint>
            {
                new() { PointId = "p1", Label = "Left arm", Type = SvgControlPointType.Move },
                new() { PointId = "p2", Label = "Outline",  Type = SvgControlPointType.StrokeAlpha },
            },
        };
        Assert.Equal(2, svg.ControlPoints!.Count);
    }

    // ── AssetSource stamping ───────────────────────────────────────────────────

    [Fact]
    public void CatalogItem_Source_DefaultIsLocalOpfs()
    {
        var item = new VideoAssetCatalogItem();
        Assert.Equal(AssetSource.LocalOpfs, item.Source);
    }

    [Fact]
    public void CatalogItem_Source_CanBeStampedAsSharedCatalog()
    {
        var item = new VideoAssetCatalogItem { Source = AssetSource.SharedCatalog };
        Assert.Equal(AssetSource.SharedCatalog, item.Source);
    }

    // ── SvgControlPoint ────────────────────────────────────────────────────────

    [Fact]
    public void SvgControlPoint_StrokeAlpha_MinMaxConstraints()
    {
        var pt = new SvgControlPoint
        {
            PointId  = "outline",
            Label    = "Outline opacity",
            Type     = SvgControlPointType.StrokeAlpha,
            MinValue = 0.0,
            MaxValue = 1.0,
            DefaultValue = 1.0,
        };
        Assert.Equal(0.0, pt.MinValue);
        Assert.Equal(1.0, pt.MaxValue);
        Assert.Equal(1.0, pt.DefaultValue);
    }

    [Fact]
    public void SvgControlPoint_FillAlpha_IndependentOfStrokeAlpha()
    {
        // Demonstrates the key scenario: stroke and fill controlled independently
        var strokePt = new SvgControlPoint
        {
            PointId = "stroke", Type = SvgControlPointType.StrokeAlpha, DefaultValue = 1.0,
        };
        var fillPt = new SvgControlPoint
        {
            PointId = "fill", Type = SvgControlPointType.FillAlpha, DefaultValue = 1.0,
        };

        Assert.NotEqual(strokePt.Type, fillPt.Type);
        Assert.Equal(SvgControlPointType.StrokeAlpha, strokePt.Type);
        Assert.Equal(SvgControlPointType.FillAlpha,   fillPt.Type);
    }

    [Fact]
    public void SvgControlPoint_Move_CanHaveNullConstraints()
    {
        var pt = new SvgControlPoint { PointId = "arm", Type = SvgControlPointType.Move };
        Assert.Null(pt.MinValue);
        Assert.Null(pt.MaxValue);
    }

    [Fact]
    public void SvgControlPoint_TargetSelector_DefaultsToWholeSvg()
    {
        var pt = new SvgControlPoint();
        Assert.Equal("*", pt.TargetSelector);
    }

    [Fact]
    public void SvgControlPoint_Color_AllowedColorsCanBeNull()
    {
        var pt = new SvgControlPoint { Type = SvgControlPointType.FillColor };
        Assert.Null(pt.AllowedColors);   // null = unrestricted color picker
    }

    // ── VideoAssetSettings ────────────────────────────────────────────────────

    [Fact]
    public void VideoAssetSettings_FlattenOnExport_DefaultTrue()
    {
        var s = new VideoAssetSettings();
        Assert.True(s.FlattenOnExport);
    }

    [Fact]
    public void VideoAssetSettings_FullyEnabled_AllFlagsTrue()
    {
        var s = new VideoAssetSettings
        {
            AllowRecolor       = true,
            AllowResize        = true,
            AllowOpacity       = true,
            AllowRotation      = true,
            AllowEffects       = true,
            AllowEasing        = true,
            AllowMotion        = true,
            AllowControlPoints = true,
        };
        Assert.True(s.AllowRecolor);
        Assert.True(s.AllowControlPoints);
    }

    [Fact]
    public void VideoAssetSettings_PresetColors_NullMeansUnrestricted()
    {
        var s = new VideoAssetSettings { AllowRecolor = true };
        Assert.Null(s.PresetColors);
    }

    // ── VideoWatermarkConfig ──────────────────────────────────────────────────

    [Fact]
    public void WatermarkConfig_Disabled_NoFileRequired()
    {
        var cfg = new VideoWatermarkConfig { Enabled = false };
        Assert.False(cfg.Enabled);
        Assert.Null(cfg.FileUrl);
    }

    [Fact]
    public void WatermarkConfig_Defaults_BottomRightAt15Percent()
    {
        var cfg = new VideoWatermarkConfig();
        Assert.Equal(WatermarkPosition.BottomRight, cfg.Position);
        Assert.Equal(0.15,  cfg.ScaleFraction);
        Assert.Equal(0.5,   cfg.Opacity);
        Assert.Equal(20,    cfg.MarginX);
        Assert.Equal(20,    cfg.MarginY);
    }

    [Fact]
    public void WatermarkConfig_CanBeTopLeft_FullOpacity()
    {
        var cfg = new VideoWatermarkConfig
        {
            Enabled       = true,
            FileUrl       = "https://example.com/wm.png",
            Version       = "abc123",
            Position      = WatermarkPosition.TopLeft,
            Opacity       = 1.0,
            ScaleFraction = 0.1,
        };
        Assert.Equal(WatermarkPosition.TopLeft, cfg.Position);
        Assert.Equal(1.0, cfg.Opacity);
    }

    // ── AssetSyncResult ───────────────────────────────────────────────────────

    [Fact]
    public void AssetSyncResult_HasChanges_TrueWhenAdded()
    {
        var result = new AssetSyncResult
        {
            Added = [new VideoAssetCatalogItem { Id = "new" }],
        };
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void AssetSyncResult_HasChanges_FalseWhenAllUnchanged()
    {
        var result = new AssetSyncResult { Unchanged = 5 };
        Assert.False(result.HasChanges);
    }

    [Fact]
    public void AssetSyncResult_HasChanges_TrueWhenWatermarkChanged()
    {
        var result = new AssetSyncResult { WatermarkChanged = true };
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void AssetSyncResult_Offline_HasOfflineReason()
    {
        var result = new AssetSyncResult
        {
            IsOffline     = true,
            OfflineReason = "Network unreachable",
        };
        Assert.True(result.IsOffline);
        Assert.NotNull(result.OfflineReason);
    }

    // ── LocalAssetEntry ───────────────────────────────────────────────────────

    [Fact]
    public void LocalAssetEntry_ServerRemoved_RetainsLocalCopy()
    {
        var entry = new LocalAssetEntry
        {
            Item             = new VideoAssetCatalogItem { Id = "x" },
            IsLocalAvailable = true,
            IsServerRemoved  = true,
        };
        // Local copy kept even after server removal
        Assert.True(entry.IsLocalAvailable);
        Assert.True(entry.IsServerRemoved);
    }

    [Fact]
    public void LocalAssetEntry_NeverDownloaded_LastDownloadedAtNull()
    {
        var entry = new LocalAssetEntry { Item = new VideoAssetCatalogItem() };
        Assert.Null(entry.LastDownloadedAt);
        Assert.False(entry.IsLocalAvailable);
    }

    // ── Enum coverage ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(VideoAssetType.Clipart)]
    [InlineData(VideoAssetType.Callout)]
    [InlineData(VideoAssetType.Shape)]
    [InlineData(VideoAssetType.Frame)]
    [InlineData(VideoAssetType.Texture)]
    [InlineData(VideoAssetType.Sticker)]
    [InlineData(VideoAssetType.Watermark)]
    public void VideoAssetType_AllValuesDefinable(VideoAssetType type)
    {
        var item = new VideoAssetCatalogItem { Type = type };
        Assert.Equal(type, item.Type);
    }

    [Theory]
    [InlineData(VideoAssetFormat.Svg)]
    [InlineData(VideoAssetFormat.Avif)]
    [InlineData(VideoAssetFormat.Png)]
    [InlineData(VideoAssetFormat.WebP)]
    [InlineData(VideoAssetFormat.Gif)]
    [InlineData(VideoAssetFormat.Lottie)]
    public void VideoAssetFormat_AllValuesDefinable(VideoAssetFormat format)
    {
        var item = new VideoAssetCatalogItem { Format = format };
        Assert.Equal(format, item.Format);
    }

    [Theory]
    [InlineData(SvgControlPointType.Move)]
    [InlineData(SvgControlPointType.Scale)]
    [InlineData(SvgControlPointType.ScaleX)]
    [InlineData(SvgControlPointType.ScaleY)]
    [InlineData(SvgControlPointType.Rotate)]
    [InlineData(SvgControlPointType.StrokeAlpha)]
    [InlineData(SvgControlPointType.FillAlpha)]
    [InlineData(SvgControlPointType.FullAlpha)]
    [InlineData(SvgControlPointType.StrokeColor)]
    [InlineData(SvgControlPointType.FillColor)]
    [InlineData(SvgControlPointType.StrokeWidth)]
    public void SvgControlPointType_AllValuesDefinable(SvgControlPointType type)
    {
        var pt = new SvgControlPoint { Type = type };
        Assert.Equal(type, pt.Type);
    }

    [Theory]
    [InlineData(AssetSource.LocalOpfs)]
    [InlineData(AssetSource.AccountLibrary)]
    [InlineData(AssetSource.SharedCatalog)]
    public void AssetSource_AllValuesDefinable(AssetSource source)
    {
        var item = new VideoAssetCatalogItem { Source = source };
        Assert.Equal(source, item.Source);
    }
}
