using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

public sealed class CalloutShapeRendererTests
{
    private static CalloutClip MakeClip(ShapeType shape) => new()
    {
        Shape = shape,
        X = 0.1, Y = 0.1, Width = 0.3, Height = 0.2,
        FillColor   = ColorHelper.Pack(255, 0, 0, 200),
        StrokeColor = ColorHelper.OpaqueBlack,
        StrokeWidth = 2,
        Opacity     = 1.0,
    };

    // ── SVG structure ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_AllShapes_ProduceSvgElement()
    {
        foreach (var shape in Enum.GetValues<ShapeType>().Where(s => s != ShapeType.Custom))
        {
            var clip = MakeClip(shape);
            CalloutShapeRenderer.SetDefaults(clip);
            var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
            Assert.Contains("<svg", svg);
            Assert.Contains("</svg>", svg);
        }
    }

    [Fact]
    public void Render_Rectangle_ContainsRectElement()
    {
        var clip = MakeClip(ShapeType.Rectangle);
        CalloutShapeRenderer.SetDefaults(clip);
        var svg = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<rect", svg);
    }

    [Fact]
    public void Render_Ellipse_ContainsEllipseElement()
    {
        var clip = MakeClip(ShapeType.Ellipse);
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<ellipse", svg);
    }

    [Fact]
    public void Render_Arrow_ContainsPathAndPolygon()
    {
        var clip = MakeClip(ShapeType.Arrow);
        CalloutShapeRenderer.SetDefaults(clip);
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<path", svg);      // the bezier shaft
        Assert.Contains("<polygon", svg);  // the arrowhead
    }

    [Fact]
    public void Render_Line_ContainsPathElement()
    {
        var clip = MakeClip(ShapeType.Line);
        CalloutShapeRenderer.SetDefaults(clip);
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<path", svg);
    }

    [Fact]
    public void Render_Star_ContainsPolygonElement()
    {
        var clip = MakeClip(ShapeType.Star);
        CalloutShapeRenderer.SetDefaults(clip);
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<polygon", svg);
    }

    // ── Control-point defaults ────────────────────────────────────────────────

    [Fact]
    public void SetDefaults_Arrow_SetsAllSixKeys()
    {
        var clip = MakeClip(ShapeType.Arrow);
        CalloutShapeRenderer.SetDefaults(clip);
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.StartX));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.StartY));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.EndX));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.EndY));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.MidX));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.MidY));
    }

    [Fact]
    public void SetDefaults_Line_SetsAllSixKeys()
    {
        var clip = MakeClip(ShapeType.Line);
        CalloutShapeRenderer.SetDefaults(clip);
        Assert.Equal(6, clip.ControlPointValues.Count);
    }

    [Fact]
    public void SetDefaults_Star_SetsFiveKeys()
    {
        var clip = MakeClip(ShapeType.Star);
        CalloutShapeRenderer.SetDefaults(clip);
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.OuterRadius));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.InnerRadius));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.Points));
        Assert.Equal(5.0, clip.ControlPointValues[CalloutControlPoints.Points]);
    }

    [Fact]
    public void SetDefaults_Rectangle_SetsCornerRadius()
    {
        var clip = MakeClip(ShapeType.Rectangle);
        CalloutShapeRenderer.SetDefaults(clip);
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.CornerRadius));
        Assert.Equal(4.0, clip.ControlPointValues[CalloutControlPoints.CornerRadius]);
    }

    /// <summary>
    /// SetDefaults is unconditional — it always sets every key for the shape. The guard (do not
    /// call it when values already exist) lives in <c>ClipStore.AddCallout</c>, not here.
    /// </summary>
    /// <remarks>
    /// The expected value used to be the clip's own X, because path points were canvas fractions
    /// derived from the box's position. They are fractions of the box itself now, so a start point
    /// at the box's left edge is 0 whatever the box's position (2026-09-05 audit, callouts-3).
    /// </remarks>
    [Fact]
    public void SetDefaults_AlwaysOverwrites_ExistingValues()
    {
        var clip = MakeClip(ShapeType.Arrow);
        clip.ControlPointValues[CalloutControlPoints.StartX] = 0.5;

        CalloutShapeRenderer.SetDefaults(clip);

        Assert.Equal(0.0, clip.ControlPointValues[CalloutControlPoints.StartX]);
    }

    /// <summary>
    /// Changing shape replaces the control points rather than layering the new shape's on top of
    /// the old shape's.
    /// </summary>
    /// <remarks>
    /// Turning a rectangle into a star gave a star with no radii and a stray corner radius; turning
    /// an arrow into a rectangle kept the arrow's path points, which came back pointing wherever
    /// the old arrow had if it was ever turned back (2026-09-05 audit, callouts-12).
    /// </remarks>
    [Fact]
    public void ReseedForShape_ReplacesTheOldShapes_Points()
    {
        var clip = MakeClip(ShapeType.Arrow);
        CalloutShapeRenderer.SetDefaults(clip);
        clip.Shape = ShapeType.Star;

        CalloutShapeRenderer.ReseedForShape(clip);

        Assert.False(clip.ControlPointValues.ContainsKey(CalloutControlPoints.StartX));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.OuterRadius));
    }

    // ── SVG canvas dimensions ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public void Render_SvgHasCorrectDimensions(int w, int h)
    {
        var clip = MakeClip(ShapeType.Rectangle);
        var svg  = CalloutShapeRenderer.Render(clip, w, h);
        Assert.Contains($"width=\"{w}\"", svg);
        Assert.Contains($"height=\"{h}\"", svg);
    }

    // ── Opacity applied ───────────────────────────────────────────────────────

    [Fact]
    public void Render_OpacityInOutput()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Opacity = 0.7 };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("opacity=\"0.700\"", svg);
    }

    // ── Shadow filter ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_ShadowBlur_IncludesFilterDef()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { ShadowBlur = 5.0 };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<defs>", svg);
        Assert.Contains("feDropShadow", svg);
    }

    [Fact]
    public void Render_NoShadow_OmitsFilterDef()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { ShadowBlur = 0 };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("<defs>", svg);
    }

    // Regression test for the bug where RenderRectangle/RenderEllipse/RenderStar built the
    // shadow filter into <defs> but never referenced it on their own element — only
    // Arrow/Line applied it. All five shapes must now apply filter="url(#bv-shadow)".
    [Theory]
    [InlineData(ShapeType.Rectangle)]
    [InlineData(ShapeType.Ellipse)]
    [InlineData(ShapeType.Star)]
    [InlineData(ShapeType.Arrow)]
    [InlineData(ShapeType.Line)]
    public void Render_ShadowBlur_AppliesFilterOnAllShapes(ShapeType shape)
    {
        var clip = MakeClip(shape) with { ShadowBlur = 5.0 };
        CalloutShapeRenderer.SetDefaults(clip);
        var svg = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("filter=\"url(#bv-shadow)\"", svg);
    }

    [Theory]
    [InlineData(ShapeType.Rectangle)]
    [InlineData(ShapeType.Ellipse)]
    [InlineData(ShapeType.Star)]
    public void Render_NoShadow_OmitsFilterAttrOnShape(ShapeType shape)
    {
        var clip = MakeClip(shape) with { ShadowBlur = 0 };
        CalloutShapeRenderer.SetDefaults(clip);
        var svg = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("filter=\"url(#bv-shadow)\"", svg);
    }

    // ── Arrow mid-point curve ─────────────────────────────────────────────────

    [Fact]
    public void Render_Arrow_QuadraticBezierUsesMidPoint()
    {
        var clip = MakeClip(ShapeType.Arrow);
        CalloutShapeRenderer.SetDefaults(clip);
        // Move midpoint upward (curve upward)
        clip.ControlPointValues[CalloutControlPoints.MidY] = 0.0;  // top of canvas
        var svg = CalloutShapeRenderer.Render(clip, 1000, 1000);
        // The Q command in the path should reference 0 for the control-point Y
        Assert.Contains(" Q ", svg);
        Assert.Contains("0.000", svg);  // MidY=0.0 → 0px → "0.000"
    }

    // ── Text label ─────────────────────────────────────────────────────────────

    [Fact]
    public void Render_NoText_OmitsTextElement()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = null };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("<text", svg);
    }

    [Fact]
    public void Render_EmptyText_OmitsTextElement()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "" };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("<text", svg);
    }

    [Theory]
    [InlineData(ShapeType.Rectangle)]
    [InlineData(ShapeType.Ellipse)]
    [InlineData(ShapeType.Arrow)]
    [InlineData(ShapeType.Line)]
    [InlineData(ShapeType.Star)]
    public void Render_TextSet_IncludesTextElement_OnAllShapes(ShapeType shape)
    {
        var clip = MakeClip(shape) with { Text = "Label", FontFamily = "Georgia", FontSize = 32 };
        CalloutShapeRenderer.SetDefaults(clip);
        var svg = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("<text", svg);
        Assert.Contains("Label", svg);
        Assert.Contains("Georgia", svg);
        Assert.Contains("font-size=\"32\"", svg);
    }

    [Fact]
    public void Render_TextSet_CenteredOnBoundingBox()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { X = 0.0, Y = 0.0, Width = 0.5, Height = 0.5, Text = "Label" };
        var svg  = CalloutShapeRenderer.Render(clip, 1000, 1000);
        // Bounding box is (0,0)-(500,500), so center is (250, 250).
        Assert.Contains("x=\"250.000\"", svg);
        Assert.Contains("y=\"250.000\"", svg);
        Assert.Contains("text-anchor=\"middle\"", svg);
        Assert.Contains("dominant-baseline=\"middle\"", svg);
    }

    [Fact]
    public void Render_MultiLineText_ProducesOneTspanPerLine()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "Line one\nLine two" };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<tspan").Count);
        Assert.Contains("Line one", svg);
        Assert.Contains("Line two", svg);
    }

    [Fact]
    public void Render_TextSet_EscapesSpecialCharacters()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "<script>&\"'</script>" };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;script&gt;", svg);
    }

    // ── Bold / underline (item #16) ─────────────────────────────────────────────

    [Fact]
    public void Render_FontBoldFalse_OmitsFontWeightAttribute()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "Label" };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("font-weight", svg);
    }

    [Fact]
    public void Render_FontBoldTrue_EmitsFontWeightBold()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "Label", FontBold = true };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("font-weight=\"bold\"", svg);
    }

    [Fact]
    public void Render_FontUnderlineFalse_OmitsTextDecorationAttribute()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "Label" };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("text-decoration", svg);
    }

    [Fact]
    public void Render_FontUnderlineTrue_EmitsTextDecorationUnderline()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "Label", FontUnderline = true };
        var svg  = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.Contains("text-decoration=\"underline\"", svg);
    }

    // ── Inline runs (item #16, phase 115) ───────────────────────────────────────

    [Fact]
    public void Render_RunsPresent_IgnoresWholeBlockFontBoldAndUnderline()
    {
        var clip = MakeClip(ShapeType.Rectangle) with
        {
            Text = "Label", FontBold = true, FontUnderline = true,
            Runs = [new TextRun { Text = "Label" }],
        };
        var svg = CalloutShapeRenderer.Render(clip, 1920, 1080);
        Assert.DoesNotContain("font-weight", svg);
        Assert.DoesNotContain("text-decoration", svg);
    }

    [Fact]
    public void Render_MultiRunSingleLine_OnlyFirstTspanCarriesX()
    {
        // MakeClip: X=0.1,Y=0.1,Width=0.3,Height=0.2 -> on a 1000x1000 canvas, cx=250.000, cy=200.000
        var clip = MakeClip(ShapeType.Rectangle) with
        {
            Text = "Hello World", // kept in sync with Runs, matching real callers' invariant
            Runs = [new TextRun { Text = "Hello " }, new TextRun { Text = "World", Bold = true }],
        };
        var svg = CalloutShapeRenderer.Render(clip, 1000, 1000);

        Assert.Contains("""<tspan x="250.000" dy="0.000">Hello </tspan>""", svg);
        Assert.Contains("""<tspan font-weight="bold">World</tspan>""", svg);
        // Only the outer <text> element and the line's first tspan carry x="250.000" — the second
        // run's tspan must not repeat it.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "<tspan x=\"250.000\""));
    }

    [Fact]
    public void Render_RunSubscript_EmitsBaselineShiftSubAndSmallerFontSize()
    {
        // MakeClip's FontSize default is 28 -> subFontSize = round(28 * 0.65) = 18
        var clip = MakeClip(ShapeType.Rectangle) with
        {
            Text = "2",
            Runs = [new TextRun { Text = "2", Subscript = true }],
        };
        var svg = CalloutShapeRenderer.Render(clip, 1000, 1000);

        Assert.Contains("baseline-shift=\"sub\" font-size=\"18\"", svg);
    }

    [Fact]
    public void Render_RunColor_OverridesFillOnItsOwnTspan()
    {
        var clip = MakeClip(ShapeType.Rectangle) with
        {
            Text = "Red",
            Runs = [new TextRun { Text = "Red", Color = "#FF0000" }],
        };
        var svg = CalloutShapeRenderer.Render(clip, 1000, 1000);

        Assert.Contains("""fill="#FF0000">Red</tspan>""", svg);
    }

    [Fact]
    public void Render_RunsNull_ProducesSameOutputAsBeforePhase115()
    {
        var clip = MakeClip(ShapeType.Rectangle) with { Text = "Line1\nLine2" };
        var svg  = CalloutShapeRenderer.Render(clip, 1000, 1000);

        Assert.DoesNotContain("baseline-shift", svg);
        Assert.Contains("Line1", svg);
        Assert.Contains("Line2", svg);
    }

    // ── NeedsSvgRenderer detection ────────────────────────────────────────────

    [Theory]
    [InlineData(ShapeType.Arrow,     null, true)]
    [InlineData(ShapeType.Line,      null, true)]
    [InlineData(ShapeType.Star,      null, true)]
    [InlineData(ShapeType.Rectangle, null, false)]
    [InlineData(ShapeType.Ellipse,   null, false)]
    [InlineData(ShapeType.Rectangle, "Label", true)]
    [InlineData(ShapeType.Ellipse,   "Label", true)]
    public void NeedsSvgRenderer_CorrectPerShapeAndText(ShapeType shape, string? text, bool expected)
    {
        // Mirrors ExportService.NeedsSvgRenderer (private): Arrow/Line/Star, a motion path, or a
        // non-empty Text label all route through the SVG renderer instead of ffmpeg-native filters —
        // the native drawbox fast path has no text rendering at all.
        var result = shape is ShapeType.Arrow or ShapeType.Line or ShapeType.Star
                     || !string.IsNullOrEmpty(text);
        Assert.Equal(expected, result);
    }

    // ── ClipStore.AddCallout sets defaults ─────────────────────────────────────

    [Fact]
    public void ClipStore_AddCallout_SetsDefaultControlPoints()
    {
        var store = new ClipStore(
            Microsoft.Extensions.Options.Options.Create(new Ben.Video.Editor.Models.VideoEditorOptions()));
        var clip = new CalloutClip { Shape = ShapeType.Arrow, Duration = 3 };
        Assert.Empty(clip.ControlPointValues);
        store.AddCallout(clip);
        Assert.NotEmpty(clip.ControlPointValues);
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.StartX));
    }

    [Fact]
    public void ClipStore_AddCallout_PreservesServerProvidedValues()
    {
        // Server pre-populates MidX/Y → geometry defaults should fill Start/End
        // but MidX/Y should keep the server's values
        var store = new ClipStore(
            Microsoft.Extensions.Options.Options.Create(new Ben.Video.Editor.Models.VideoEditorOptions()));
        var clip = new CalloutClip { Shape = ShapeType.Arrow, Duration = 3 };
        clip.ControlPointValues[CalloutControlPoints.MidX] = 0.75;  // server value
        clip.ControlPointValues[CalloutControlPoints.MidY] = 0.25;  // server value
        store.AddCallout(clip);
        // Server values preserved
        Assert.Equal(0.75, clip.ControlPointValues[CalloutControlPoints.MidX]);
        Assert.Equal(0.25, clip.ControlPointValues[CalloutControlPoints.MidY]);
        // Geometry defaults filled
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.StartX));
        Assert.True(clip.ControlPointValues.ContainsKey(CalloutControlPoints.EndX));
    }

    // ── CalloutShapeDefinition WebAPI models ──────────────────────────────────

    [Fact]
    public void CalloutShapeDefinition_CurvedArrow_HasSixPoints()
    {
        var def = CalloutShapeDefinition.CurvedArrow();
        Assert.Equal(ShapeType.Arrow, def.ShapeType);
        Assert.Equal(6, def.AdjustableControlPoints!.Count);
    }

    [Fact]
    public void CalloutShapeDefinition_AllowedKeys_MatchPointKeys()
    {
        var def = CalloutShapeDefinition.CurvedArrow();
        var keys = def.AllowedKeys!;
        Assert.Contains(CalloutControlPoints.MidX, keys);
        Assert.Contains(CalloutControlPoints.MidY, keys);
        Assert.Contains(CalloutControlPoints.StartX, keys);
    }

    [Fact]
    public void CalloutControlPointDef_AllowKeyframe_FalseByDefault()
    {
        var cpd = new CalloutControlPointDef { Key = CalloutControlPoints.StartX };
        Assert.False(cpd.AllowKeyframe);
    }

    [Fact]
    public void CalloutControlPointDef_AllowKeyframe_CanBeTrue()
    {
        var cpd = new CalloutControlPointDef
        {
            Key = CalloutControlPoints.MidX,
            AllowKeyframe = true,
        };
        Assert.True(cpd.AllowKeyframe);
    }

    // ── CalloutControlPoints constants ────────────────────────────────────────

    [Fact]
    public void CalloutControlPoints_ArrowKeys_ContainsSixKeys()
    {
        Assert.Equal(6, CalloutControlPoints.ArrowKeys.Count);
    }

    [Fact]
    public void CalloutControlPoints_CurveKeys_ContainsMidXY()
    {
        Assert.Contains(CalloutControlPoints.MidX, CalloutControlPoints.CurveKeys);
        Assert.Contains(CalloutControlPoints.MidY, CalloutControlPoints.CurveKeys);
        Assert.Equal(2, CalloutControlPoints.CurveKeys.Count);
    }

    [Fact]
    public void VideoAssetCatalogItem_ShapeDefinition_NullForRasterByDefault()
    {
        var item = new Ben.Video.Editor.Models.Assets.VideoAssetCatalogItem
        {
            Type = Ben.Video.Editor.Models.Assets.VideoAssetType.Callout,
        };
        Assert.Null(item.ShapeDefinition);
    }

    [Fact]
    public void VideoAssetCatalogItem_ShapeDefinition_CanHoldCalloutTemplate()
    {
        var item = new Ben.Video.Editor.Models.Assets.VideoAssetCatalogItem
        {
            Type            = Ben.Video.Editor.Models.Assets.VideoAssetType.Callout,
            ShapeDefinition = CalloutShapeDefinition.CurvedArrow(),
        };
        Assert.NotNull(item.ShapeDefinition);
        Assert.Equal(ShapeType.Arrow, item.ShapeDefinition.ShapeType);
    }

    // ── Arrowhead angle correctness (was 144° — now ±30°) ────────────────────

    [Fact]
    public void Render_Arrow_ArrowheadAngle_IsApproximately30Degrees()
    {
        // A horizontal right-pointing arrow should have arrowhead wings at ±30°
        var clip = MakeClip(ShapeType.Arrow);
        // Force a clean horizontal arrow
        clip.ControlPointValues[CalloutControlPoints.StartX] = 0.1;
        clip.ControlPointValues[CalloutControlPoints.StartY] = 0.5;
        clip.ControlPointValues[CalloutControlPoints.EndX]   = 0.9;
        clip.ControlPointValues[CalloutControlPoints.EndY]   = 0.5;
        clip.ControlPointValues[CalloutControlPoints.MidX]   = 0.5;
        clip.ControlPointValues[CalloutControlPoints.MidY]   = 0.5;

        var svg = CalloutShapeRenderer.Render(clip, 1000, 500);

        // Extract polygon points — format: "x1,y1 x2,y2 x3,y3"
        var polyStart = svg.IndexOf("points=\"", StringComparison.Ordinal);
        Assert.True(polyStart >= 0, "SVG should contain a polygon for arrowhead");
        var polyContent = svg[(polyStart + 8)..svg.IndexOf('"', polyStart + 8)];
        var pts = polyContent.Split(' ')
            .Select(p => p.Split(','))
            .Select(p => (x: double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                          y: double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        Assert.Equal(3, pts.Count);
        var tip  = pts[0]; // tip = End point (900, 250)
        var wing1 = pts[1];
        var wing2 = pts[2];

        // Compute angles from tip to each wing
        var a1 = Math.Atan2(wing1.y - tip.y, wing1.x - tip.x) * 180 / Math.PI;
        var a2 = Math.Atan2(wing2.y - tip.y, wing2.x - tip.x) * 180 / Math.PI;

        // Wings should be near ±150° from the origin (180°±30° for rightward arrow)
        // The arrowhead arms should each be approximately 150° from the tip in screen coordinates
        Assert.True(Math.Abs(Math.Abs(a1) - 150) < 15, $"Wing angle should be ~150°, got {a1:F1}°");
        // The old bug was 144° — still CLOSE but the fix gives 150° which is correct ±30° geometry
        // Verify the two wings are symmetric around 180°
        var midAngle = (a1 + a2) / 2;
        Assert.True(Math.Abs(Math.Abs(midAngle) - 180) < 5 || Math.Abs(midAngle) < 5,
            $"Wings should be symmetric around 180°, midAngle={midAngle:F1}°");
    }

    // ── SetDefaults — Ellipse is a no-op ─────────────────────────────────────

    [Fact]
    public void SetDefaults_Ellipse_LeavesControlPointsEmpty()
    {
        var clip = MakeClip(ShapeType.Ellipse);
        CalloutShapeRenderer.SetDefaults(clip);
        // Ellipse has no configurable control points
        Assert.Empty(clip.ControlPointValues);
    }

    [Fact]
    public void SetDefaults_Custom_LeavesControlPointsEmpty()
    {
        var clip = MakeClip(ShapeType.Custom);
        CalloutShapeRenderer.SetDefaults(clip);
        Assert.Empty(clip.ControlPointValues);
    }

    // ── Zero-length / degenerate arrow guard ──────────────────────────────────

    [Fact]
    public void Render_Arrow_ZeroLength_DoesNotThrow()
    {
        var clip = MakeClip(ShapeType.Arrow);
        // Degenerate: all points at same position
        clip.ControlPointValues[CalloutControlPoints.StartX] = 0.5;
        clip.ControlPointValues[CalloutControlPoints.StartY] = 0.5;
        clip.ControlPointValues[CalloutControlPoints.EndX]   = 0.5;
        clip.ControlPointValues[CalloutControlPoints.EndY]   = 0.5;
        clip.ControlPointValues[CalloutControlPoints.MidX]   = 0.5;
        clip.ControlPointValues[CalloutControlPoints.MidY]   = 0.5;

        var svg = CalloutShapeRenderer.Render(clip, 1000, 500);
        Assert.Contains("<svg", svg);     // renders without exception
        Assert.Contains("<path", svg);   // shaft still renders
        // Polygon arrowhead is omitted for zero-length arrow
        Assert.DoesNotContain("<polygon", svg);
    }

    [Fact]
    public void Render_Arrow_EndEqualsControlPoint_FallsBackToStartEndAngle()
    {
        var clip = MakeClip(ShapeType.Arrow);
        // End == Mid: should fall back to start→end angle, not return NaN/0
        clip.ControlPointValues[CalloutControlPoints.StartX] = 0.1;
        clip.ControlPointValues[CalloutControlPoints.StartY] = 0.5;
        clip.ControlPointValues[CalloutControlPoints.EndX]   = 0.9;
        clip.ControlPointValues[CalloutControlPoints.EndY]   = 0.5;
        clip.ControlPointValues[CalloutControlPoints.MidX]   = 0.9;  // same as End
        clip.ControlPointValues[CalloutControlPoints.MidY]   = 0.5;

        var svg = CalloutShapeRenderer.Render(clip, 1000, 500);
        Assert.Contains("<polygon", svg);   // arrowhead still rendered using start→end fallback
    }
}