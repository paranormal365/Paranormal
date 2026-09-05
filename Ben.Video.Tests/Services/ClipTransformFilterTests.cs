using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Placing a clip somewhere other than the whole frame.
/// </summary>
/// <remarks>
/// A clip could only ever fill the frame, so a second camera could replace the picture underneath
/// and never sit beside it or in a corner of it — and portrait phone footage stayed on its side
/// with a DVR's black bars still in shot (2026-09-05 audit, the completeness critic's
/// picture-in-picture and crop/rotate items).
/// </remarks>
public sealed class ClipTransformFilterTests
{
    [Fact]
    public void A_clip_that_fills_the_frame_needs_no_extra_work()
    {
        Assert.False(ClipTransformFilter.NeedsWork(null));
        Assert.False(ClipTransformFilter.NeedsWork(new ClipTransform()));
        Assert.Null(ClipTransformFilter.BuildSourceChain(new ClipTransform(), 1920, 1080));
    }

    /// <summary>A corner inset: a quarter-size picture placed in the top right.</summary>
    [Fact]
    public void A_corner_inset_is_scaled_and_offset()
    {
        var transform = new ClipTransform { X = 0.7, Y = 0.05, Width = 0.25, Height = 0.25 };

        var chain = ClipTransformFilter.BuildSourceChain(transform, 1920, 1080)!;
        var (x, y) = ClipTransformFilter.Offset(transform, 1920, 1080);

        Assert.Contains("scale=480:270", chain);
        Assert.Equal(1344, x);
        Assert.Equal(54, y);
    }

    /// <summary>
    /// The picture keeps its proportions inside the box it was given. A plain scale would stretch
    /// a 16:9 camera into whatever shape the box happened to be.
    /// </summary>
    [Fact]
    public void The_picture_keeps_its_shape_inside_its_box()
    {
        var chain = ClipTransformFilter.BuildSourceChain(
            new ClipTransform { Width = 0.5, Height = 0.5 }, 1920, 1080)!;

        Assert.Contains("force_original_aspect_ratio=decrease", chain);
        Assert.Contains("pad=", chain);
    }

    /// <summary>Side by side: two half-width clips, one at each end.</summary>
    [Fact]
    public void Two_clips_can_sit_side_by_side()
    {
        var left  = new ClipTransform { X = 0.0, Width = 0.5 };
        var right = new ClipTransform { X = 0.5, Width = 0.5 };

        Assert.Equal(0,   ClipTransformFilter.Offset(left,  1920, 1080).X);
        Assert.Equal(960, ClipTransformFilter.Offset(right, 1920, 1080).X);
    }

    /// <summary>
    /// The crop comes before everything else, so what is scaled into the box is the part being
    /// kept rather than the part being cut off.
    /// </summary>
    [Fact]
    public void A_crop_is_taken_before_the_picture_is_scaled()
    {
        var chain = ClipTransformFilter.BuildSourceChain(
            new ClipTransform { CropTop = 0.1, CropBottom = 0.1 }, 1920, 1080)!;

        Assert.StartsWith("crop=", chain);
        Assert.True(chain.IndexOf("crop=", StringComparison.Ordinal)
                  < chain.IndexOf("scale=", StringComparison.Ordinal));
    }

    [Fact]
    public void A_crop_keeps_what_was_not_cut_off()
    {
        var chain = ClipTransformFilter.BuildSourceChain(
            new ClipTransform { CropLeft = 0.25, CropRight = 0.25 }, 1920, 1080)!;

        Assert.Contains("crop=iw*0.500000:ih*1.000000:iw*0.250000:ih*0.000000", chain);
    }

    /// <summary>
    /// A crop that would leave nothing is clamped. It is a mistake rather than an instruction, and
    /// handing ffmpeg a zero width loses the whole render.
    /// </summary>
    [Fact]
    public void A_crop_that_would_leave_nothing_still_leaves_something()
    {
        var chain = ClipTransformFilter.BuildSourceChain(
            new ClipTransform { CropLeft = 0.8, CropRight = 0.8 }, 1920, 1080)!;

        Assert.Contains("crop=iw*0.020000", chain);
    }

    /// <summary>
    /// Turning portrait footage upright: the output grows to hold the turned picture instead of
    /// cutting its corners off, and the new corners are transparent.
    /// </summary>
    [Fact]
    public void Rotation_grows_the_picture_rather_than_clipping_it()
    {
        var chain = ClipTransformFilter.BuildSourceChain(
            new ClipTransform { Rotation = 90 }, 1920, 1080)!;

        Assert.Contains("rotate=", chain);
        Assert.Contains("ow=rotw(", chain);
        Assert.Contains("c=black@0.0", chain);
    }

    [Fact]
    public void Opacity_is_applied_to_the_placed_picture()
    {
        var chain = ClipTransformFilter.BuildSourceChain(
            new ClipTransform { Opacity = 0.5 }, 1920, 1080)!;

        Assert.Contains("colorchannelmixer=aa=0.5000", chain);
    }

    /// <summary>
    /// Every size and offset is even. Chroma-subsampled output cannot land on an odd boundary, and
    /// ffmpeg either refuses or shifts by a pixel.
    /// </summary>
    [Theory]
    [InlineData(0.333, 0.777)]
    [InlineData(0.1234, 0.4321)]
    public void Every_number_handed_to_ffmpeg_is_even(double x, double width)
    {
        var transform = new ClipTransform { X = x, Y = x, Width = width, Height = width };

        var chain  = ClipTransformFilter.BuildSourceChain(transform, 1920, 1080)!;
        var (ox, oy) = ClipTransformFilter.Offset(transform, 1920, 1080);
        var scale  = chain.Split("scale=")[1].Split(':').Take(2).Select(int.Parse).ToArray();

        Assert.Equal(0, ox % 2);
        Assert.Equal(0, oy % 2);
        Assert.Equal(0, scale[0] % 2);
        Assert.Equal(0, scale[1] % 2);
    }
}
