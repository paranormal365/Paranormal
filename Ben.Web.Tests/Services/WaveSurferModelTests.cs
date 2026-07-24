using Ben.Service.Models.Entities;
using Ben.Web.Library.Manage.Audio;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for WsAudioSource, WsConfig, WsOptions defaults,
/// and UploadFileAudioConfigExtensions.ToWsConfig().
/// </summary>
public class WaveSurferModelTests
{
    // ── WsAudioSource ─────────────────────────────────────────────────────────

    [Fact]
    public void FromUrl_SetsTypeAndUrl()
    {
        var src = WsAudioSource.FromUrl("https://cdn.example.com/audio.mp3");

        Assert.Equal(WsAudioSourceType.Url, src.Type);
        Assert.Equal("https://cdn.example.com/audio.mp3", src.Url);
    }

    [Fact]
    public void FromUrl_IsValid_WhenUrlIsNonEmpty()
    {
        Assert.True(WsAudioSource.FromUrl("/api/upload-files/abc/download").IsValid);
    }

    [Fact]
    public void FromUrl_IsValid_False_WhenUrlIsEmpty()
    {
        Assert.False(WsAudioSource.FromUrl("").IsValid);
        Assert.False(WsAudioSource.FromUrl("   ").IsValid);
    }

    [Fact]
    public void FromUrl_ToLoadUrl_ReturnsUrl()
    {
        var src = WsAudioSource.FromUrl("/audio.mp3");
        Assert.Equal("/audio.mp3", src.ToLoadUrl());
    }

    [Fact]
    public void FromUrl_ToLoadUrl_ReturnsNull_WhenInvalid()
    {
        Assert.Null(WsAudioSource.FromUrl("").ToLoadUrl());
    }

    [Fact]
    public void FromBytes_SetsTypeAndContentType()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var src   = WsAudioSource.FromBytes(bytes, "audio/wav");

        Assert.Equal(WsAudioSourceType.Bytes, src.Type);
        Assert.Equal("audio/wav", src.ContentType);
        Assert.Same(bytes, src.Bytes);
    }

    [Fact]
    public void FromBytes_DefaultContentType_IsAudioMpeg()
    {
        var src = WsAudioSource.FromBytes(new byte[1]);
        Assert.Equal("audio/mpeg", src.ContentType);
    }

    [Fact]
    public void FromBytes_IsValid_WhenBytesNonEmpty()
    {
        Assert.True(WsAudioSource.FromBytes(new byte[] { 0xFF }).IsValid);
    }

    [Fact]
    public void FromBytes_IsValid_False_WhenBytesEmpty()
    {
        Assert.False(WsAudioSource.FromBytes(Array.Empty<byte>()).IsValid);
    }

    [Fact]
    public void FromBytes_ToLoadUrl_ReturnsBase64DataUrl()
    {
        var bytes  = new byte[] { 0x01, 0x02, 0x03 };
        var src    = WsAudioSource.FromBytes(bytes, "audio/wav");
        var result = src.ToLoadUrl()!;

        Assert.StartsWith("data:audio/wav;base64,", result);
        Assert.Equal(
            $"data:audio/wav;base64,{Convert.ToBase64String(bytes)}",
            result);
    }

    [Fact]
    public void FromBase64_SetsTypeAndData()
    {
        var src = WsAudioSource.FromBase64("AAAA", "audio/ogg");

        Assert.Equal(WsAudioSourceType.Base64, src.Type);
        Assert.Equal("AAAA", src.Base64);
        Assert.Equal("audio/ogg", src.ContentType);
    }

    [Fact]
    public void FromBase64_IsValid_WhenBase64NonEmpty()
    {
        Assert.True(WsAudioSource.FromBase64("AAAA").IsValid);
    }

    [Fact]
    public void FromBase64_IsValid_False_WhenBase64Empty()
    {
        Assert.False(WsAudioSource.FromBase64("").IsValid);
    }

    [Fact]
    public void FromBase64_ToLoadUrl_ReturnsDataUrl()
    {
        var src = WsAudioSource.FromBase64("AAAA", "audio/mpeg");
        Assert.Equal("data:audio/mpeg;base64,AAAA", src.ToLoadUrl());
    }

    [Fact]
    public void FromDataUrl_SetsTypeToUrl_AndPassesThrough()
    {
        const string dataUrl = "data:audio/mpeg;base64,AAAA";
        var src = WsAudioSource.FromDataUrl(dataUrl);

        Assert.Equal(WsAudioSourceType.Url, src.Type);
        Assert.Equal(dataUrl, src.Url);
        Assert.Equal(dataUrl, src.ToLoadUrl());
    }

    // ── WsOptions defaults ────────────────────────────────────────────────────

    [Fact]
    public void WsOptions_Default_ColorPropertiesAreNull()
    {
        // Colors must be null so the JS interop resolves them from Telerik CSS vars
        var opts = new WsOptions();

        Assert.Null(opts.WaveColor);
        Assert.Null(opts.ProgressColor);
        Assert.Null(opts.CursorColor);
    }

    [Fact]
    public void WsOptions_Default_FillParentAndInteractAreTrue()
    {
        var opts = new WsOptions();

        Assert.True(opts.FillParent);
        Assert.True(opts.Interact);
    }

    [Fact]
    public void WsOptions_ExplicitColors_NotOverwrittenByDefaults()
    {
        var opts = new WsOptions { WaveColor = "#FF0000", ProgressColor = "#00FF00" };

        Assert.Equal("#FF0000", opts.WaveColor);
        Assert.Equal("#00FF00", opts.ProgressColor);
    }

    // ── WsConfig factories ────────────────────────────────────────────────────

    [Fact]
    public void WsConfig_Default_EnablesHoverAndTimeline()
    {
        var cfg = WsConfig.Default();

        Assert.True(cfg.Plugins.Hover);
        Assert.True(cfg.Plugins.Timeline);
        Assert.False(cfg.Plugins.Zoom);
        Assert.False(cfg.Plugins.Minimap);
    }

    [Fact]
    public void WsConfig_Default_HasExpectedLayoutDefaults()
    {
        var cfg = WsConfig.Default();

        Assert.Equal("200px", cfg.InitialHeight);
        Assert.Equal("80px",  cfg.MinHeight);
        Assert.Equal("800px", cfg.MaxHeight);
        Assert.True(cfg.ShowControls);
        Assert.Equal(10,   cfg.MinZoom);
        Assert.Equal(1000, cfg.MaxZoom);
    }

    [Fact]
    public void WsConfig_Default_SetsSourceWhenProvided()
    {
        var source = WsAudioSource.FromUrl("/audio.mp3");
        var cfg    = WsConfig.Default(source);

        Assert.Same(source, cfg.Source);
    }

    [Fact]
    public void WsConfig_Rich_EnablesMorePlugins()
    {
        var cfg = WsConfig.Rich();

        Assert.True(cfg.Plugins.Hover);
        Assert.True(cfg.Plugins.Timeline);
        Assert.True(cfg.Plugins.Zoom);
        Assert.True(cfg.Plugins.Minimap);
    }

    [Fact]
    public void WsConfig_Compact_HasReducedHeightAndNoControls()
    {
        var cfg = WsConfig.Compact();

        Assert.Equal("100px", cfg.InitialHeight);
        Assert.Equal("60px",  cfg.MinHeight);
        Assert.False(cfg.ShowControls);
    }

    [Fact]
    public void WsConfig_Colors_AreNullByDefault()
    {
        // Both WsConfig.Default() and new WsConfig() must leave colors null
        // so they are resolved from the Telerik theme at runtime.
        Assert.Null(WsConfig.Default().Options.WaveColor);
        Assert.Null(new WsConfig().Options.WaveColor);
    }

    // ── UploadFileAudioConfigExtensions.ToWsConfig() ─────────────────────────

    private static UploadFileAudioConfigRecord MakeRecord(Action<UploadFileAudioConfigRecord>? configure = null)
    {
        var record = new UploadFileAudioConfigRecord
        {
            Id              = Guid.NewGuid(),
            UploadFileId    = Guid.NewGuid(),
            WaveColor       = null,     // null = use theme color
            ProgressColor   = null,
            CursorColor     = null,
            Height          = null,
            EnableHover     = true,
            EnableTimeline  = true,
            EnableZoom      = false,
            EnableMinimap   = false,
            EnableSpectrogram = false,
            EnableSpectrogramWindowed = false,
            EnableEnvelope  = false,
            EnableRegions   = false,
            InitialHeight   = "220px",
            MinHeight       = "90px",
            MaxHeight       = "700px",
            ShowControls    = true,
            MinZoom         = 10,
            MaxZoom         = 500,
            DateCreated     = DateTime.UtcNow,
            CreatedByAppUserId = Guid.NewGuid(),
        };
        return record;
    }

    [Fact]
    public void ToWsConfig_NullColors_RemainNull_ForThemeResolution()
    {
        var cfg = MakeRecord().ToWsConfig();

        Assert.Null(cfg.Options.WaveColor);
        Assert.Null(cfg.Options.ProgressColor);
        Assert.Null(cfg.Options.CursorColor);
    }

    [Fact]
    public void ToWsConfig_ExplicitColors_AreMapped()
    {
        var record = MakeRecord() with { WaveColor = "#3B82F6", ProgressColor = "#1D4ED8" };
        var cfg    = record.ToWsConfig();

        Assert.Equal("#3B82F6", cfg.Options.WaveColor);
        Assert.Equal("#1D4ED8", cfg.Options.ProgressColor);
    }

    [Fact]
    public void ToWsConfig_MapsPluginEnableFlags()
    {
        var record = MakeRecord() with
        {
            EnableHover    = true,
            EnableTimeline = false,
            EnableZoom     = true,
            EnableRegions  = true,
        };
        var cfg = record.ToWsConfig();

        Assert.True(cfg.Plugins.Hover);
        Assert.False(cfg.Plugins.Timeline);
        Assert.True(cfg.Plugins.Zoom);
        Assert.True(cfg.Plugins.Regions);
    }

    [Fact]
    public void ToWsConfig_MapsLayoutSettings()
    {
        var record = MakeRecord() with
        {
            InitialHeight = "280px",
            MinHeight     = "100px",
            MaxHeight     = "600px",
            ShowControls  = false,
            MinZoom       = 20,
            MaxZoom       = 800,
        };
        var cfg = record.ToWsConfig();

        Assert.Equal("280px", cfg.InitialHeight);
        Assert.Equal("100px", cfg.MinHeight);
        Assert.Equal("600px", cfg.MaxHeight);
        Assert.False(cfg.ShowControls);
        Assert.Equal(20,  cfg.MinZoom);
        Assert.Equal(800, cfg.MaxZoom);
    }

    [Fact]
    public void ToWsConfig_DeserializesZoomOptions_WhenJsonPresent()
    {
        var record = MakeRecord() with
        {
            EnableZoom    = true,
            ZoomOptionsJson = """{"scale":0.3,"maxZoom":800}""",
        };
        var cfg = record.ToWsConfig();

        Assert.NotNull(cfg.Plugins.ZoomOptions);
        Assert.Equal(0.3, cfg.Plugins.ZoomOptions!.Scale);
        Assert.Equal(800, cfg.Plugins.ZoomOptions.MaxZoom);
    }

    [Fact]
    public void ToWsConfig_NullPluginJson_LeavesOptionsNull()
    {
        var record = MakeRecord() with { ZoomOptionsJson = null };
        var cfg    = record.ToWsConfig();

        Assert.Null(cfg.Plugins.ZoomOptions);
    }

    [Fact]
    public void ToWsConfig_InvalidPluginJson_ReturnsNullGracefully()
    {
        // Should not throw — bad JSON is silently treated as no options
        var record = MakeRecord() with { ZoomOptionsJson = "not-valid-json{{{" };
        var cfg    = record.ToWsConfig();   // must not throw

        Assert.Null(cfg.Plugins.ZoomOptions);
    }

    [Fact]
    public void ToWsConfig_SourceParameter_IsSetOnConfig()
    {
        var source = WsAudioSource.FromUrl("/api/upload-files/xyz/download");
        var cfg    = MakeRecord().ToWsConfig(source);

        Assert.Same(source, cfg.Source);
    }

    [Fact]
    public void ToWsConfig_NullSource_LeavesSourceNull()
    {
        var cfg = MakeRecord().ToWsConfig(null);
        Assert.Null(cfg.Source);
    }

    // ── WsRegionData.Label ────────────────────────────────────────────────────

    [Fact]
    public void WsRegionData_Label_DefaultsToNull()
    {
        var r = new WsRegionData { Id = "r1", Start = 0, End = 5 };
        Assert.Null(r.Label);
    }

    [Fact]
    public void WsRegionData_Label_CanBeSet()
    {
        var r = new WsRegionData { Id = "r1", Start = 0, End = 5, Label = "Verse" };
        Assert.Equal("Verse", r.Label);
    }

    // ── WsRegionContextMenuArgs ───────────────────────────────────────────────

    [Fact]
    public void WsRegionContextMenuArgs_Properties_AreReadable()
    {
        var args = new WsRegionContextMenuArgs
        {
            RegionId = "r1",
            Start    = 10.5,
            End      = 20.0,
            Label    = "Chorus",
            ClientX  = 400,
            ClientY  = 250,
        };

        Assert.Equal("r1",     args.RegionId);
        Assert.Equal(10.5,     args.Start);
        Assert.Equal(20.0,     args.End);
        Assert.Equal("Chorus", args.Label);
        Assert.Equal(400,      args.ClientX);
        Assert.Equal(250,      args.ClientY);
    }

    [Fact]
    public void WsRegionContextMenuArgs_Label_CanBeNull()
    {
        var args = new WsRegionContextMenuArgs { RegionId = "r1", Start = 0, End = 5, Label = null };
        Assert.Null(args.Label);
    }
}
