using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Effects;

/// <summary>
/// The shadow filter's region must never be a percentage of the filtered shape's own bounding box.
/// A horizontal arrow or line has a box of zero height, so a percentage region collapses to nothing
/// and the shape is not drawn at all — the arrow a user points at their evidence with rendered as a
/// floating arrowhead with no shaft, on screen and in the exported file (2026-09-18 audit).
/// </summary>
public class SvgShadowFilterRegionTests
{
    [Fact]
    public void The_region_is_measured_in_user_space_not_in_the_shapes_own_box()
    {
        var svg = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 3, 3, 4, 1280, 720);

        Assert.Contains("filterUnits=\"userSpaceOnUse\"", svg);
        Assert.DoesNotContain("%", svg);
    }

    [Fact]
    public void The_region_covers_the_whole_canvas_and_the_shadow_that_falls_off_it()
    {
        var svg = SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 3, 3, 4, 1280, 720);

        var width  = Attr(svg, "width");
        var height = Attr(svg, "height");

        Assert.True(width  > 1280, $"region width {width} does not cover a 1280px canvas");
        Assert.True(height >  720, $"region height {height} does not cover a 720px canvas");
    }

    [Fact]
    public void A_flat_horizontal_arrow_keeps_its_shaft()
    {
        // Width but no height: the exact shape that vanished.
        var clip = new CalloutClip
        {
            Shape = ShapeType.Arrow,
            X = 0.1, Y = 0.1, Width = 0.2, Height = 0.15,
            ShadowBlur = 4,
        };

        var svg = CalloutShapeRenderer.Render(clip, 1280, 720);

        Assert.Contains("<path", svg);          // the shaft is emitted...
        Assert.Contains("<polygon", svg);       // ...alongside the head
        // ...and the filter it is handed cannot erase it.
        Assert.Contains("filterUnits=\"userSpaceOnUse\"", svg);
        Assert.DoesNotContain("width=\"140%\"", svg);
    }

    [Fact]
    public void No_shadow_still_means_no_filter_at_all()
    {
        Assert.Equal(string.Empty, SvgShadowFilter.Build(ColorHelper.OpaqueBlack, 3, 3, 0, 1280, 720));
    }

    private static double Attr(string svg, string name)
    {
        var at = svg.IndexOf($"{name}=\"", StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {name} on the filter");
        var from = at + name.Length + 2;
        var to   = svg.IndexOf('"', from);
        return double.Parse(svg[from..to], System.Globalization.CultureInfo.InvariantCulture);
    }
}
