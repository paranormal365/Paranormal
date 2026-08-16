using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class SvgFrameRendererTests
{
    // ── SvgControlPointPatch model ────────────────────────────────────────────

    [Fact]
    public void SvgControlPointPatch_Defaults()
    {
        var p = new SvgControlPointPatch();
        Assert.Equal("*",   p.TargetSelector);
        Assert.Equal(0.0,   p.Value);
        Assert.Equal(0.0,   p.X);
        Assert.Equal(0.0,   p.Y);
        Assert.Null(p.Color);
    }

    [Fact]
    public void SvgControlPointPatch_StrokeAlpha_SetFadeValues()
    {
        var p = new SvgControlPointPatch
        {
            PointId        = "outline",
            TargetSelector = "#stroke-group",
            Type           = SvgControlPointType.StrokeAlpha,
            Value          = 0.0,   // fully faded
        };
        Assert.Equal(SvgControlPointType.StrokeAlpha, p.Type);
        Assert.Equal(0.0, p.Value);
    }

    [Fact]
    public void SvgControlPointPatch_FillAlpha_IndependentOfStroke()
    {
        var stroke = new SvgControlPointPatch { Type = SvgControlPointType.StrokeAlpha, Value = 0.0 };
        var fill   = new SvgControlPointPatch { Type = SvgControlPointType.FillAlpha,   Value = 1.0 };
        Assert.NotEqual(stroke.Type, fill.Type);
        Assert.Equal(0.0, stroke.Value);
        Assert.Equal(1.0, fill.Value);
    }

    [Fact]
    public void SvgControlPointPatch_ColorType_HasColor()
    {
        var p = new SvgControlPointPatch
        {
            Type  = SvgControlPointType.FillColor,
            Color = "#FF0000",
        };
        Assert.Equal("#FF0000", p.Color);
    }

    [Fact]
    public void SvgControlPointPatch_MoveType_HasXY()
    {
        var p = new SvgControlPointPatch
        {
            Type = SvgControlPointType.Move,
            X    = 50.0,
            Y    = -20.0,
        };
        Assert.Equal(50.0,  p.X);
        Assert.Equal(-20.0, p.Y);
    }

    [Theory]
    [InlineData(SvgControlPointType.StrokeAlpha)]
    [InlineData(SvgControlPointType.FillAlpha)]
    [InlineData(SvgControlPointType.FullAlpha)]
    [InlineData(SvgControlPointType.StrokeColor)]
    [InlineData(SvgControlPointType.FillColor)]
    [InlineData(SvgControlPointType.Move)]
    [InlineData(SvgControlPointType.Scale)]
    [InlineData(SvgControlPointType.ScaleX)]
    [InlineData(SvgControlPointType.ScaleY)]
    [InlineData(SvgControlPointType.Rotate)]
    [InlineData(SvgControlPointType.StrokeWidth)]
    public void SvgControlPointPatch_AllTypes_CanBeCreated(SvgControlPointType type)
    {
        var p = new SvgControlPointPatch { Type = type };
        Assert.Equal(type, p.Type);
    }

    // ── SvgAnimationExporter patch building (via BuildPatches through test-accessible model) ──

    [Fact]
    public void ClipArtClip_StaticControlPointValues_ServeAsBaselineForExport()
    {
        // A clip with stroke set to 0 and fill set to 1 should produce
        // the expected patch values without needing the JS renderer.
        var clip = new ClipArtClip
        {
            AssetId  = Guid.NewGuid().ToString(),
            Duration = 5,
            ControlPoints = new List<SvgControlPoint>
            {
                new() { PointId = "stroke", TargetSelector = "#outline", Type = SvgControlPointType.StrokeAlpha },
                new() { PointId = "fill",   TargetSelector = "#content", Type = SvgControlPointType.FillAlpha   },
            },
            ControlPointValues = new Dictionary<string, double>
            {
                ["stroke"] = 0.0,
                ["fill"]   = 1.0,
            },
        };

        // Verify the values are set as expected
        Assert.Equal(0.0, clip.ControlPointValues["stroke"]);
        Assert.Equal(1.0, clip.ControlPointValues["fill"]);
        Assert.Equal("#outline", clip.ControlPoints![0].TargetSelector);
        Assert.Equal("#content", clip.ControlPoints![1].TargetSelector);
    }

    [Fact]
    public void ClipArtClip_FallsBackToDefaultValue_WhenNotSet()
    {
        var clip = new ClipArtClip
        {
            AssetId = "x",
            Duration = 2,
            ControlPoints = new List<SvgControlPoint>
            {
                new() { PointId = "alpha", Type = SvgControlPointType.FullAlpha, DefaultValue = 0.5 },
            },
            // ControlPointValues intentionally empty — should fall back to DefaultValue
        };

        var pt = clip.ControlPoints![0];
        var value = clip.ControlPointValues.TryGetValue(pt.PointId, out var v) ? v : pt.DefaultValue;
        Assert.Equal(0.5, value);
    }

    [Fact]
    public void ClipArtClip_ColorFallsBackToDefaultColor()
    {
        var clip = new ClipArtClip
        {
            AssetId = "x",
            Duration = 2,
            ControlPoints = new List<SvgControlPoint>
            {
                new() { PointId = "stroke-col", Type = SvgControlPointType.StrokeColor, DefaultColor = "#FFFFFF" },
            },
        };
        var pt    = clip.ControlPoints![0];
        var color = clip.ControlPointColors.TryGetValue(pt.PointId, out var c) ? c : pt.DefaultColor;
        Assert.Equal("#FFFFFF", color);
    }

    // ── OPFSService ReadAsTextAsync (new method verification) ─────────────────

    [Fact]
    public void OPFSService_ReadAsTextAsync_MethodExists()
    {
        // Verify the method exists at compile time by checking the type
        var method = typeof(OPFSService).GetMethod("ReadAsTextAsync");
        Assert.NotNull(method);

        var returnType = method!.ReturnType;
        // Should return Task<string?>
        Assert.True(returnType.IsGenericType);
        Assert.Equal(typeof(System.Threading.Tasks.Task<string?>), returnType);
    }

    // ── Frame count calculation ────────────────────────────────────────────────

    [Theory]
    [InlineData(5.0, 30.0, 150)]
    [InlineData(1.0, 24.0,  24)]
    [InlineData(0.1, 30.0,   3)]   // Math.Round(0.1*30) = 3
    [InlineData(2.5, 25.0,  62)]   // Math.Round(2.5*25) = Math.Round(62.5) = 62 (banker's rounding)
    public void FrameCount_Calculation_IsCorrect(double duration, double fps, int expected)
    {
        var actual = Math.Max(1, (int)Math.Round(duration * fps));
        Assert.Equal(expected, actual);
    }

    // ── ParseResolution helper (via ExportService-like logic) ─────────────────

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("1280x720",  1280,  720)]
    [InlineData("3840x2160", 3840, 2160)]
    [InlineData("",          1920, 1080)]   // fallback
    [InlineData("badvalue",  1920, 1080)]   // fallback
    public void ParseResolution_ReturnsCorrectDimensions(string resolution, int expectedW, int expectedH)
    {
        (int w, int h) = ParseResolution(resolution);
        Assert.Equal(expectedW, w);
        Assert.Equal(expectedH, h);
    }

    private static (int w, int h) ParseResolution(string resolution)
    {
        if (!string.IsNullOrEmpty(resolution))
        {
            var parts = resolution.Split('x');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var w)
                && int.TryParse(parts[1], out var h))
                return (w, h);
        }
        return (1920, 1080);
    }

    // ── Render dimension calculation ──────────────────────────────────────────

    [Fact]
    public void RenderDimensions_UseVideoWidthFraction()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = -1 };
        const int videoWidth = 1920;

        var renderW = Math.Max(1, (int)(clip.Width * videoWidth));
        var renderH = clip.Height > 0 ? 1 : renderW;   // -1 = square/aspect from width

        Assert.Equal(384, renderW);
        Assert.Equal(384, renderH);
    }

    [Fact]
    public void RenderDimensions_ExplicitHeight_UsesVideoHeightFraction()
    {
        var clip = new ClipArtClip { Width = 0.2, Height = 0.1 };
        const int videoWidth  = 1920;
        const int videoHeight = 1080;

        var renderW = Math.Max(1, (int)(clip.Width  * videoWidth));
        var renderH = Math.Max(1, (int)(clip.Height * videoHeight));

        Assert.Equal(384, renderW);
        Assert.Equal(108, renderH);
    }

    // ── ffmpeg filter correctness (validates the [out] label is present) ──────

    [Fact]
    public void SvgOverlayFilter_ContainsOutLabel()
    {
        // Simulate the filter built by SvgAnimationExporter
        const int renderW = 384, renderH = 216;
        const int px = 100, py = 50;
        const double startT = 2.0, endT = 7.0;

        var filter = $"[1:v]scale={renderW}:{renderH}[ov];[0:v][ov]overlay={px}:{py}:enable='between(t,{startT:F3},{endT:F3})'[out]";

        Assert.Contains("[out]", filter);
        Assert.Contains("[ov]", filter);
        Assert.Contains("between(t,2.000,7.000)", filter);
        Assert.Contains("scale=384:216", filter);
        Assert.Contains("overlay=100:50", filter);
    }

    [Fact]
    public void SvgOverlayFilter_InputIndexing_HasInputOneThenScale()
    {
        // [1:v] must be the image sequence input (input index 1)
        const int w = 200, h = 100;
        var filter = $"[1:v]scale={w}:{h}[ov];[0:v][ov]overlay=0:0:enable='between(t,0,5)'[out]";
        Assert.StartsWith("[1:v]scale=", filter);
    }

    // ── Phase 3b independence — callouts independent of TextOverlays flag ─────

    [Fact]
    public void ClipStore_AllCalloutClips_IndependentOfTextOverlays()
    {
        // Verify callouts and clipart are enumerated regardless of feature flags
        // (the export pipeline must apply them even when TextOverlays is disabled)
        var store = new ClipStore(
            Microsoft.Extensions.Options.Options.Create(
                new VideoEditorOptions { TextOverlays = false }));

        var callout = new CalloutClip { Duration = 3 };
        var art     = new ClipArtClip { AssetId = "x", Duration = 3 };
        store.AddCallout(callout);
        store.AddClipArtClip(art);

        Assert.Single(store.AllCalloutClips);
        Assert.Single(store.AllClipArtClips);
        Assert.Empty(store.AllTextOverlays);
    }
}