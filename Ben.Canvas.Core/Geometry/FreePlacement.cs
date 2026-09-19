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
/// Close in it steps finely, to use a gap beside a neighbour; further out it steps a block at a
/// time, so a crowded board fills in rows rather than running out of places to look.
/// Pure geometry, like <see cref="BesidePlacement"/>, so the rule is tested without a board.
/// </para>
/// </remarks>
public static class FreePlacement
{
    /// <summary>How far each ring is from the last — a comfortable sliver of ground between blocks.</summary>
    public const double Step = 32;

    /// <summary>How many fine rings to try before widening the search to a lattice.</summary>
    public const int Rings = 24;

    /// <summary>
    /// How many block-sized rings to walk after the fine ones, before giving up honestly.
    /// </summary>
    /// <remarks>
    /// The fine rings try eight compass points each, and a block is far wider than their 32 px step,
    /// so along any one direction only every seventh ring or so clears the last block — about
    /// **thirty-two** distinct spots in all. Additions past that landed on the wanted spot, which is
    /// to say on top of each other: a board driven to 321 blocks put 290 of them on one square
    /// (measured 2026-09-17). A lattice of the block's own size cannot run out that way, and this
    /// many rings holds over four thousand blocks.
    /// </remarks>
    public const int LatticeRings = 32;

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

        // Near first, in fine steps: a block should slot into the gap beside its neighbours rather
        // than be flung a lattice cell away when there is room right there.
        for (var ring = 1; ring <= Rings; ring++)
        {
            var d = ring * Step;
            foreach (var (dx, dy) in Around(d))
            {
                var candidate = new WorldRect(wanted.X + dx, wanted.Y + dy, width, height);
                if (!others.Any(candidate.Intersects)) return candidate;
            }
        }

        // Then a lattice of the block's own size, which lays out a crowded board in rows instead of
        // running out of compass points and stacking everything on the wanted spot.
        var cellX = width + BesidePlacement.Gap;
        var cellY = height + BesidePlacement.Gap;
        for (var ring = 1; ring <= LatticeRings; ring++)
        {
            foreach (var (column, row) in Lattice(ring))
            {
                var candidate = new WorldRect(wanted.X + column * cellX, wanted.Y + row * cellY, width, height);
                if (!others.Any(candidate.Intersects)) return candidate;
            }
        }

        return wanted;
    }

    /// <summary>
    /// The cells exactly <paramref name="ring"/> steps out, bottom row first and left to right
    /// within a row — the same downward reading order the fine rings use.
    /// </summary>
    private static IEnumerable<(int column, int row)> Lattice(int ring)
    {
        for (var row = ring; row >= -ring; row--)
        {
            // The rows between the bottom and the top only have their two end cells on this ring.
            if (row == ring || row == -ring)
            {
                for (var column = -ring; column <= ring; column++) yield return (column, row);
            }
            else
            {
                yield return (-ring, row);
                yield return (ring, row);
            }
        }
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
