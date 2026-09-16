using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Geometry;

/// <summary>
/// Where a new block goes when it is made from the Add sheet: as near the middle of the view as
/// the board allows, and never on top of what is already there.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "Add a free space nudge when adding a new card." A block added from the sheet
/// used to land at the exact centre of the view, which on a board with anything in it meant on
/// top of it — the new card hid the old one and the old one's title was the first thing to move.
/// </para>
/// <para>
/// The search walks outward from the wanted spot in rings, trying below and to the right before
/// above and to the left, so a run of additions lays down in reading order rather than scattering.
/// Pure geometry, like <see cref="BesidePlacement"/>, so the rule is tested without a board.
/// </para>
/// </remarks>
public static class FreePlacement
{
    /// <summary>How far each ring is from the last — a comfortable sliver of ground between blocks.</summary>
    public const double Step = 32;

    /// <summary>How many rings to try before giving up and using the wanted spot anyway.</summary>
    public const int Rings = 24;

    /// <summary>The breathing room kept between the new block and its neighbours.</summary>
    public const double Clearance = BesidePlacement.Gap / 4;

    /// <summary>
    /// A <paramref name="width"/> by <paramref name="height"/> rectangle centred on <paramref name="centre"/>,
    /// or the nearest spot to it that is clear of <paramref name="taken"/>.
    /// </summary>
    public static WorldRect Near(CanvasPoint centre, double width, double height, IEnumerable<WorldRect> taken)
    {
        var wanted = new WorldRect(centre.X - width / 2, centre.Y - height / 2, width, height);
        var others = taken.Where(r => !r.IsEmpty).Select(r => r.Inflate(Clearance)).ToList();
        if (others.Count == 0 || !others.Any(wanted.Intersects)) return wanted;

        for (var ring = 1; ring <= Rings; ring++)
        {
            var d = ring * Step;
            foreach (var (dx, dy) in Around(d))
            {
                var candidate = new WorldRect(wanted.X + dx, wanted.Y + dy, width, height);
                if (!others.Any(candidate.Intersects)) return candidate;
            }
        }

        return wanted;
    }

    /// <summary>
    /// The eight points of a ring, nearest-to-the-eye first: straight down, straight right, then the
    /// diagonals below, then up and left — the order a reader would look for "the next thing".
    /// </summary>
    private static IEnumerable<(double dx, double dy)> Around(double d)
    {
        yield return (0, d);
        yield return (d, 0);
        yield return (d, d);
        yield return (-d, d);
        yield return (0, -d);
        yield return (-d, 0);
        yield return (d, -d);
        yield return (-d, -d);
    }
}
