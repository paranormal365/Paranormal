using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Geometry;

/// <summary>What a point on the board landed on.</summary>
public enum CanvasHitKind { None, Node, Group }

/// <summary>The result of a board hit test.</summary>
public readonly record struct CanvasHit(CanvasHitKind Kind, Guid? Id)
{
    public static CanvasHit None => new(CanvasHitKind.None, null);
}

/// <summary>
/// Point-in-rectangle hit testing for blocks and groups.
/// </summary>
/// <remarks>
/// Copied from Ben.Video.Editor's CanvasHitTester, in world coordinates. Axis-aligned: blocks do not
/// rotate. Connectors are curves and are hit-tested by <see cref="BezierHitTester"/>, only when this finds
/// nothing, so a block always wins over a line drawn beneath it.
/// </remarks>
public static class CanvasHitTester
{
    /// <summary>The index of the first rectangle containing the point, or null. Order rects topmost first.</summary>
    public static int? HitTest(IReadOnlyList<WorldRect> rectsTopmostFirst, double wx, double wy)
    {
        for (var i = 0; i < rectsTopmostFirst.Count; i++)
            if (rectsTopmostFirst[i].Contains(wx, wy))
                return i;
        return null;
    }

    public static WorldRect RectOf(CanvasNode n) => new(n.X, n.Y, n.Width, n.Height);

    public static WorldRect RectOf(CanvasGroup g) => new(g.X, g.Y, g.Width, g.Height);

    /// <summary>The front-most block under the point. Locked blocks count: they can still be selected.</summary>
    public static CanvasNode? TopmostNodeAt(IEnumerable<CanvasNode> nodes, double wx, double wy)
    {
        var ordered = nodes.OrderByDescending(n => n.Z).ThenByDescending(n => n.Id).ToList();
        var index = HitTest(ordered.Select(RectOf).ToList(), wx, wy);
        return index is { } i ? ordered[i] : null;
    }

    /// <summary>Blocks first, then groups: a point on a member hits the member; inside the group but on no block, the group.</summary>
    public static CanvasHit HitAt(CanvasDocument doc, double wx, double wy)
    {
        if (TopmostNodeAt(doc.Nodes, wx, wy) is { } node) return new(CanvasHitKind.Node, node.Id);

        var group = doc.Groups.OrderByDescending(g => g.Z).FirstOrDefault(g => RectOf(g).Contains(wx, wy));
        return group is null ? CanvasHit.None : new(CanvasHitKind.Group, group.Id);
    }

    /// <summary>Blocks a marquee touches. Touching is enough, as in Obsidian and Figma.</summary>
    public static IReadOnlyList<Guid> NodesIn(IEnumerable<CanvasNode> nodes, WorldRect marquee) =>
        nodes.Where(n => RectOf(n).Intersects(marquee)).Select(n => n.Id).ToList();
}

/// <summary>Which blocks are worth rendering on a very large board.</summary>
public static class VisibleNodeFilter
{
    /// <summary>
    /// Blocks intersecting the view inflated by <paramref name="paddingFraction"/> of its size, so a block
    /// scrolled just off screen is already there when it comes back.
    /// </summary>
    public static IReadOnlyList<CanvasNode> Visible(IEnumerable<CanvasNode> nodes, WorldRect viewWorld, double paddingFraction = 0.5)
    {
        var pad = Math.Max(viewWorld.Width, viewWorld.Height) * paddingFraction;
        var area = viewWorld.Inflate(pad);
        return nodes.Where(n => CanvasHitTester.RectOf(n).Intersects(area)).ToList();
    }
}

/// <summary>Which group a block belongs in, by where its centre is.</summary>
public static class GroupMembership
{
    /// <summary>The front-most group whose rectangle contains the block's centre, ignoring excluded groups.</summary>
    public static Guid? GroupContaining(IEnumerable<CanvasGroup> groups, WorldRect nodeRect, IReadOnlySet<Guid>? excludeGroupIds = null) =>
        groups
            .Where(g => excludeGroupIds is null || !excludeGroupIds.Contains(g.Id))
            .OrderByDescending(g => g.Z)
            .FirstOrDefault(g => CanvasHitTester.RectOf(g).Contains(nodeRect.CenterX, nodeRect.CenterY))
            ?.Id;
}
