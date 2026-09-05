using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Obscuring part of a picture, so a clip with one identifying detail in it can still be shown.
/// </summary>
/// <remarks>
/// The failure that matters is a box in the wrong place: a redaction that misses is worse than no
/// redaction, because the person who drew it believes the face is covered.
/// </remarks>
public sealed class RedactionFilterTests
{
    private static RedactionRegion Region(
        double x = 0.25, double y = 0.25, double w = 0.5, double h = 0.5,
        RedactionStyle style = RedactionStyle.Blur, double strength = 6) =>
        new() { X = x, Y = y, Width = w, Height = h, Style = style, Strength = strength };

    [Fact]
    public void Nothing_to_redact_is_no_filter_at_all()
        => Assert.Null(RedactionFilter.Build([], 1920, 1080));

    [Fact]
    public void One_region_is_cropped_where_it_was_drawn()
    {
        var graph = RedactionFilter.Build([Region()], 1000, 1000);

        Assert.NotNull(graph);
        Assert.Contains("crop=500:500:250:250", graph);
    }

    /// <summary>
    /// The graph has to end at [vout] because that is what the export maps.
    /// </summary>
    [Fact]
    public void The_result_is_named_for_the_export_to_map()
    {
        var graph = RedactionFilter.Build([Region(), Region(x: 0.05, y: 0.05, w: 0.1, h: 0.1)], 1000, 1000);

        Assert.EndsWith("[vout]", graph);
    }

    [Fact]
    public void Each_region_gets_its_own_copy_of_the_frame()
    {
        var graph = RedactionFilter.Build(
            [Region(), Region(x: 0.05, y: 0.05, w: 0.1, h: 0.1), Region(x: 0.7, y: 0.7, w: 0.2, h: 0.2)],
            1000, 1000);

        Assert.Contains("split=4", graph);
    }

    [Fact]
    public void Blur_blurs_and_pixelate_does_not()
    {
        Assert.Contains("gblur", RedactionFilter.Build([Region()], 1000, 1000));
        Assert.DoesNotContain("gblur",
            RedactionFilter.Build([Region(style: RedactionStyle.Pixelate)], 1000, 1000));
    }

    [Fact]
    public void Pixelate_scales_down_and_back_up_without_smoothing()
    {
        var graph = RedactionFilter.Build([Region(style: RedactionStyle.Pixelate)], 1000, 1000);

        Assert.Contains("flags=neighbor", graph);
        Assert.Contains("scale=500:500:flags=neighbor", graph);
    }

    /// <summary>
    /// A stronger setting has to actually hide more, or the control is decoration.
    /// </summary>
    [Fact]
    public void More_strength_blurs_harder()
    {
        var light = RedactionFilter.Build([Region(strength: 1)], 1000, 1000)!;
        var heavy = RedactionFilter.Build([Region(strength: 10)], 1000, 1000)!;

        var lightSigma = double.Parse(light.Split("gblur=sigma=")[1].Split(']')[0].Split('[')[0]);
        var heavySigma = double.Parse(heavy.Split("gblur=sigma=")[1].Split(']')[0].Split('[')[0]);

        Assert.True(heavySigma > lightSigma);
    }

    /// <summary>
    /// A small face has to be hidden as thoroughly as a large one, so the blur follows the size of
    /// the region rather than being a fixed radius.
    /// </summary>
    [Fact]
    public void A_bigger_region_is_blurred_more_heavily()
    {
        var small = RedactionFilter.Build([Region(w: 0.05, h: 0.05)], 1000, 1000)!;
        var large = RedactionFilter.Build([Region(w: 0.5,  h: 0.5)],  1000, 1000)!;

        var smallSigma = double.Parse(small.Split("gblur=sigma=")[1].Split('[')[0]);
        var largeSigma = double.Parse(large.Split("gblur=sigma=")[1].Split('[')[0]);

        Assert.True(largeSigma > smallSigma);
    }

    /// <summary>
    /// Crops must sit on even boundaries. Chroma-subsampled output cannot crop on an odd one, and
    /// ffmpeg either refuses or shifts by a pixel — which on a redaction leaves the edge of what
    /// was being hidden visible.
    /// </summary>
    [Theory]
    [InlineData(0.333, 0.777)]
    [InlineData(0.1234, 0.4321)]
    [InlineData(0.5001, 0.0999)]
    public void Every_crop_number_is_even(double x, double y)
    {
        var graph = RedactionFilter.Build([Region(x: x, y: y, w: 0.211, h: 0.311)], 1920, 1080)!;
        var crop  = graph.Split("crop=")[1].Split(',')[0].Split(':');

        Assert.All(crop, n => Assert.Equal(0, int.Parse(n) % 2));
    }

    /// <summary>
    /// A region running off the edge is pulled back inside. ffmpeg refuses a crop that starts in
    /// the frame and ends outside it, and losing the whole render to that is not acceptable when
    /// the render is somebody's evidence reel.
    /// </summary>
    [Fact]
    public void A_region_hanging_off_the_edge_stays_inside_the_frame()
    {
        var graph = RedactionFilter.Build([Region(x: 0.9, y: 0.9, w: 0.5, h: 0.5)], 1000, 1000)!;
        var crop  = graph.Split("crop=")[1].Split(',')[0].Split(':').Select(int.Parse).ToArray();

        Assert.True(crop[2] + crop[0] <= 1000);
        Assert.True(crop[3] + crop[1] <= 1000);
    }

    /// <summary>
    /// A box too small to hide anything is dropped rather than handed to ffmpeg, which refuses a
    /// zero-sized crop and takes the export down with it.
    /// </summary>
    [Fact]
    public void A_region_with_no_area_is_dropped_rather_than_rendered()
        => Assert.Null(RedactionFilter.Build([Region(w: 0.0001, h: 0.0001)], 1000, 1000));

    [Fact]
    public void And_dropping_one_does_not_drop_the_others()
    {
        var graph = RedactionFilter.Build(
            [Region(w: 0.0001, h: 0.0001), Region()], 1000, 1000)!;

        Assert.Contains("split=2", graph);
        Assert.EndsWith("[vout]", graph);
    }
}
