using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Tests.Commands;

/// <summary>
/// Where a block added from the sheet lands: the middle of the view, unless something is there.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16: "Add a free space nudge when adding a new card." The card used to land on top
/// of whatever sat at the centre of the view.
/// </remarks>
public sealed class FreePlacementTests
{
    private static readonly CanvasPoint Centre = new(500, 400);

    [Fact]
    public void An_empty_board_takes_the_middle_of_the_view()
    {
        var placed = FreePlacement.Near(Centre, 200, 120, []);

        Assert.Equal(Centre.X, placed.CenterX);
        Assert.Equal(Centre.Y, placed.CenterY);
    }

    [Fact]
    public void A_clear_middle_is_used_even_when_the_board_has_blocks_elsewhere()
    {
        var placed = FreePlacement.Near(Centre, 200, 120, [new WorldRect(0, 0, 200, 120)]);

        Assert.Equal(Centre.X, placed.CenterX);
        Assert.Equal(Centre.Y, placed.CenterY);
    }

    [Fact]
    public void A_block_at_the_middle_pushes_the_new_one_clear_of_it_and_nearby()
    {
        var sitting = new WorldRect(Centre.X - 100, Centre.Y - 60, 200, 120);

        var placed = FreePlacement.Near(Centre, 200, 120, [sitting]);

        Assert.False(placed.Intersects(sitting.Inflate(FreePlacement.Clearance)));
        // Nearby: within a few rings of where it was asked for, not flung to a corner.
        Assert.True(Math.Abs(placed.CenterX - Centre.X) <= FreePlacement.Step * 6);
        Assert.True(Math.Abs(placed.CenterY - Centre.Y) <= FreePlacement.Step * 6);
    }

    [Fact]
    public void Below_is_tried_before_above_so_additions_read_downward()
    {
        var sitting = new WorldRect(Centre.X - 100, Centre.Y - 60, 200, 120);

        var placed = FreePlacement.Near(Centre, 200, 120, [sitting]);

        Assert.True(placed.Y > sitting.Y, "the new block should be below the one it found in its way");
    }

    [Fact]
    public void Several_additions_never_land_on_each_other()
    {
        var taken = new List<WorldRect>();
        for (var i = 0; i < 12; i++)
        {
            var placed = FreePlacement.Near(Centre, 200, 120, taken);
            Assert.DoesNotContain(taken, t => placed.Intersects(t));
            taken.Add(placed);
        }
    }

    /// <summary>
    /// A board full of blocks keeps getting clear ground, rather than piling the rest on one square.
    /// </summary>
    /// <remarks>
    /// Twelve was as far as this went, and twelve is comfortably inside what the fine rings reach.
    /// Past about thirty-two they ran out of compass points and every addition after that landed on
    /// the wanted spot — a board driven to 321 blocks had 290 of them on one square, and the drag
    /// test on a 300-block board failed because the block it meant to take hold of was buried
    /// (2026-09-17). Two hundred is past the old ceiling by a wide margin.
    /// </remarks>
    [Fact]
    public void Two_hundred_additions_each_get_ground_of_their_own()
    {
        var taken = new List<WorldRect>();
        for (var i = 0; i < 200; i++)
        {
            var placed = FreePlacement.Near(Centre, 200, 120, taken);
            Assert.DoesNotContain(taken, t => placed.Intersects(t));
            taken.Add(placed);
        }

        Assert.Equal(200, taken.Select(t => (t.X, t.Y)).Distinct().Count());
    }

    /// <summary>
    /// Blocks of different sizes share one board, so the lattice is never trusted on its own: every
    /// candidate is still measured against what is already there.
    /// </summary>
    [Fact]
    public void Blocks_of_different_sizes_still_never_overlap()
    {
        var taken = new List<WorldRect>();
        var sizes = new (double W, double H)[] { (200, 120), (333, 240), (280, 200), (200, 120) };

        for (var i = 0; i < 80; i++)
        {
            var (w, h) = sizes[i % sizes.Length];
            var placed = FreePlacement.Near(Centre, w, h, taken);
            Assert.DoesNotContain(taken, t => placed.Intersects(t));
            taken.Add(placed);
        }
    }

    [Fact]
    public void A_board_with_no_room_at_all_still_answers_with_the_wanted_spot()
    {
        // One enormous block: nothing within the rings is clear, and the rule gives up honestly
        // rather than placing the block a screen away.
        var wall = new WorldRect(-10_000, -10_000, 20_000, 20_000);

        var placed = FreePlacement.Near(Centre, 200, 120, [wall]);

        Assert.Equal(Centre.X, placed.CenterX);
        Assert.Equal(Centre.Y, placed.CenterY);
    }
}
