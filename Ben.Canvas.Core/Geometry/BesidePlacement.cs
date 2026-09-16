using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Geometry;

/// <summary>
/// Where a new block goes when it is made from an existing one's side.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "Can we make it like Miro?" On those boards a block's side handle makes the next
/// block already joined to this one, so a train of thought is laid down without ever aiming at
/// anything. Everything in that gesture is placement: the new block has to land where the eye
/// expects, and it must not land on top of something that is already there.
/// </para>
/// <para>
/// Pure geometry, so the rule can be tested without a board, a renderer or a browser.
/// </para>
/// </remarks>
public static class BesidePlacement
{
    /// <summary>The space left between the two blocks — wide enough for the connector to read as a line.</summary>
    public const double Gap = 64;

    /// <summary>How far each attempt shifts along the other axis when the first choice is occupied.</summary>
    private const double Step = 24;

    /// <summary>How many shifts to try before giving up and using the first choice anyway.</summary>
    private const int Attempts = 40;

    /// <summary>
    /// A rectangle of <paramref name="width"/> by <paramref name="height"/> on <paramref name="side"/>
    /// of <paramref name="from"/>, lined up with it and clear of <paramref name="taken"/>.
    /// </summary>
    /// <remarks>
    /// Clear of, not merely not-overlapping: the search uses an inflated copy of each existing block,
    /// so the new one never comes to rest pressed up against its neighbour's edge.
    /// </remarks>
    public static WorldRect For(WorldRect from, CanvasSide side, double width, double height, IEnumerable<WorldRect> taken)
    {
        var first = FirstChoice(from, side, width, height);
        var others = taken.Where(r => !r.IsEmpty).Select(r => r.Inflate(Gap / 4)).ToList();
        if (others.Count == 0) return first;

        // Sideways for a block placed left or right, downward for one placed above or below: the
        // shift is always along the axis the gesture did not choose, so the new block stays in the
        // direction it was asked for.
        var (stepX, stepY) = side is CanvasSide.Left or CanvasSide.Right ? (0d, Step) : (Step, 0d);

        for (var attempt = 0; attempt <= Attempts; attempt++)
        {
            // Away from the source first, then back the other way, so a crowded board fills evenly
            // rather than always pushing one direction.
            foreach (var sign in attempt == 0 ? new[] { 0 } : [1, -1])
            {
                var candidate = new WorldRect(
                    first.X + stepX * attempt * sign,
                    first.Y + stepY * attempt * sign,
                    width, height);

                if (!others.Any(candidate.Intersects)) return candidate;
            }
        }

        return first;
    }

    /// <summary>Straight out from the side, centred on the source across the other axis.</summary>
    private static WorldRect FirstChoice(WorldRect from, CanvasSide side, double width, double height) => side switch
    {
        CanvasSide.Right => new(from.Right + Gap, from.CenterY - height / 2, width, height),
        CanvasSide.Left => new(from.X - Gap - width, from.CenterY - height / 2, width, height),
        CanvasSide.Bottom => new(from.CenterX - width / 2, from.Bottom + Gap, width, height),
        _ => new(from.CenterX - width / 2, from.Y - Gap - height, width, height),
    };

    /// <summary>The side a connector should land on: the one facing back the way it came.</summary>
    public static CanvasSide Opposite(CanvasSide side) => side switch
    {
        CanvasSide.Right => CanvasSide.Left,
        CanvasSide.Left => CanvasSide.Right,
        CanvasSide.Bottom => CanvasSide.Top,
        _ => CanvasSide.Bottom,
    };
}
