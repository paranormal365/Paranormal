using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// An arrow callout's points belong to the callout, not to the frame.
/// </summary>
/// <remarks>
/// They were canvas fractions with no relationship to the shape that owned them, so moving,
/// resizing or animating a callout left its arrow exactly where it was (2026-09-05 audit,
/// callouts-3).
/// </remarks>
public sealed class CalloutControlPointSpaceTests
{
    [Fact]
    public void A_box_fraction_lands_inside_the_box()
        => Assert.Equal(0.35, CalloutControlPointSpace.ToCanvas(0.5, 0.2, 0.3), 6);

    [Fact]
    public void And_converts_back()
        => Assert.Equal(0.5, CalloutControlPointSpace.FromCanvas(0.35, 0.2, 0.3), 6);

    /// <summary>
    /// Outside 0–1 is allowed. An arrow pointing out of its own box is an ordinary thing to draw.
    /// </summary>
    [Fact]
    public void A_point_outside_the_box_survives_the_round_trip()
    {
        var canvas = CalloutControlPointSpace.ToCanvas(1.5, 0.2, 0.3);

        Assert.Equal(1.5, CalloutControlPointSpace.FromCanvas(canvas, 0.2, 0.3), 6);
    }

    /// <summary>
    /// A box with no size has no inside, so there is no fraction to give. Keeping the value where
    /// it was drawn beats sending the arrow to infinity.
    /// </summary>
    [Fact]
    public void A_box_with_no_size_leaves_the_value_alone()
        => Assert.Equal(0.4, CalloutControlPointSpace.FromCanvas(0.4, 0.2, 0.0), 6);

    /// <summary>
    /// The heart of it: an older project's points are re-expressed so the arrow stays drawn where
    /// it was, and now moves with its callout.
    /// </summary>
    [Fact]
    public void An_older_callouts_points_are_re_expressed_against_its_box()
    {
        var clip = new CalloutClip
        {
            Name = "arrow", Shape = ShapeType.Arrow,
            X = 0.2, Y = 0.4, Width = 0.4, Height = 0.2,
            ControlPointValues = new Dictionary<string, double>
            {
                [CalloutControlPoints.StartX] = 0.2,  // the box's left edge, in canvas terms
                [CalloutControlPoints.StartY] = 0.5,  // halfway down it
                [CalloutControlPoints.EndX]   = 0.6,  // its right edge
                [CalloutControlPoints.EndY]   = 0.5,
            },
        };

        CalloutControlPointSpace.MigrateToBoxRelative(clip);

        Assert.Equal(0.0, clip.ControlPointValues[CalloutControlPoints.StartX], 6);
        Assert.Equal(0.5, clip.ControlPointValues[CalloutControlPoints.StartY], 6);
        Assert.Equal(1.0, clip.ControlPointValues[CalloutControlPoints.EndX], 6);
        Assert.Equal(0.5, clip.ControlPointValues[CalloutControlPoints.EndY], 6);
    }

    /// <summary>
    /// Only the path points moved space. A star's radii and a rectangle's corner were already
    /// measured against the box, and rewriting them would move them for no reason.
    /// </summary>
    [Fact]
    public void A_stars_radii_are_left_alone()
    {
        var clip = new CalloutClip
        {
            Name = "star", Shape = ShapeType.Star,
            X = 0.2, Y = 0.4, Width = 0.4, Height = 0.2,
            ControlPointValues = new Dictionary<string, double>
            {
                [CalloutControlPoints.OuterRadius] = 0.9,
                [CalloutControlPoints.InnerRadius] = 0.4,
            },
        };

        CalloutControlPointSpace.MigrateToBoxRelative(clip);

        Assert.Equal(0.9, clip.ControlPointValues[CalloutControlPoints.OuterRadius], 6);
        Assert.Equal(0.4, clip.ControlPointValues[CalloutControlPoints.InnerRadius], 6);
    }

    /// <summary>
    /// The behaviour the whole change exists for: move the callout, and its arrow goes with it.
    /// </summary>
    [Fact]
    public void Moving_a_callout_moves_its_arrow()
    {
        var clip = new CalloutClip
        {
            Name = "arrow", Shape = ShapeType.Arrow,
            X = 0.1, Y = 0.1, Width = 0.2, Height = 0.2,
        };
        CalloutShapeRenderer.SetDefaults(clip);

        var before = CalloutShapeRenderer.Render(clip, 1000, 1000);
        clip.X += 0.5;
        var after = CalloutShapeRenderer.Render(clip, 1000, 1000);

        Assert.NotEqual(before, after);
    }
}
