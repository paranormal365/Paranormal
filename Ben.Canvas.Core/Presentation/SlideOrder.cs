using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Presentation;

/// <summary>One stop in a walk through a board: what to frame, and what to call it.</summary>
/// <param name="Id">The group or block this stop shows.</param>
/// <param name="IsGroup">Whether <paramref name="Id"/> names a group rather than a block.</param>
/// <param name="Rect">What the view is fitted to.</param>
/// <param name="Title">What the stop is called, for the counter and for a screen reader.</param>
public sealed record Slide(Guid Id, bool IsGroup, WorldRect Rect, string Title);

/// <summary>
/// The order a board is walked through when somebody presents it.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-16: "presentation mode like miro where you can create the cards like slides". The
/// cards ARE the slides — there is nothing extra to make first. So the order has to be one the board
/// already expresses, or presenting would mean laying the whole thing out again in a side panel.
/// </para>
/// <para>Three rules, in this order:</para>
/// <list type="number">
///   <item><b>Groups win.</b> A group is a labelled rectangle somebody drew round part of the board —
///   the same thing Miro calls a frame. When a board has any, they are the slides, and the loose
///   blocks between them are not stops of their own.</item>
///   <item><b>Otherwise, follow the arrows.</b> A connector says "and then this", and the side-handle
///   gesture that makes the next card writes exactly that. So a chain of joined cards is walked from
///   its start to its end, in order.</item>
///   <item><b>Everything else reads down the page</b>, top to bottom and left to right, as a page of
///   anything else does. Chains take their place in that order from where their first card sits.</item>
/// </list>
/// <para>
/// Pure: given a document it returns the same list every time, with no view, no renderer and no
/// browser involved, so the rules above can be read back from the tests.
/// </para>
/// </remarks>
public static class SlideOrder
{
    /// <summary>How much room is left around a slide when the view is fitted to it.</summary>
    public const double Padding = 48;

    /// <summary>A chain longer than this is a cycle somebody drew; it is walked once and let go.</summary>
    private const int MostStops = 10_000;

    /// <summary>The stops, in the order they are shown. Empty for an empty board.</summary>
    /// <param name="document">The board.</param>
    /// <param name="title">
    /// What to call a block. The editor passes the same namer the board announces blocks with, so a
    /// slide is called what its card is called; without one, a block is named for its kind.
    /// </param>
    public static IReadOnlyList<Slide> For(CanvasDocument document, Func<CanvasNode, string>? title = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        title ??= n => BlockRegistry.Get(n.Type).DisplayName;

        var groups = document.Groups ?? [];
        if (groups.Count > 0)
        {
            return [.. groups
                .Select(g => new Slide(g.Id, true, Pad(CanvasHitTester.RectOf(g)), Named(g)))
                .OrderBy(s => s.Rect.Y).ThenBy(s => s.Rect.X)];
        }

        var nodes = (document.Nodes ?? []).Where(n => n is not null).ToList();
        if (nodes.Count == 0) return [];

        var byId = nodes.ToDictionary(n => n.Id);
        var edges = (document.Edges ?? [])
            .Where(e => e is not null && byId.ContainsKey(e.FromNodeId) && byId.ContainsKey(e.ToNodeId) && e.FromNodeId != e.ToNodeId)
            .ToList();

        // One step forward per block: a card with two arrows out is a fork, and a presentation cannot
        // take both. The first by reading order wins, so the choice is the one the board looks like.
        var next = edges
            .GroupBy(e => e.FromNodeId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => byId[e.ToNodeId]).OrderBy(n => n.Y).ThenBy(n => n.X).First().Id);

        var pointedAt = edges.Select(e => e.ToNodeId).ToHashSet();

        var placed = new HashSet<Guid>();
        var slides = new List<Slide>();

        // Starts first, in reading order: a chain begins at the card nothing points at. A ring of
        // cards has no such card, and its members are picked up by the sweep below.
        foreach (var start in nodes.Where(n => !pointedAt.Contains(n.Id)).OrderBy(n => n.Y).ThenBy(n => n.X))
            Walk(start.Id);

        foreach (var node in nodes.OrderBy(n => n.Y).ThenBy(n => n.X))
            Walk(node.Id);

        return slides;

        void Walk(Guid id)
        {
            for (var stops = 0; stops < MostStops; stops++)
            {
                if (!placed.Add(id)) return;                 // already shown: the chain has met itself
                var node = byId[id];
                slides.Add(new Slide(node.Id, false, Pad(CanvasHitTester.RectOf(node)), title(node)));
                if (!next.TryGetValue(id, out var onward)) return;
                id = onward;
            }
        }
    }

    /// <summary>Room around the thing being shown, so a slide is never flush against the edge.</summary>
    private static WorldRect Pad(WorldRect rect) =>
        new(rect.X - Padding, rect.Y - Padding, rect.Width + 2 * Padding, rect.Height + 2 * Padding);

    private static string Named(CanvasGroup group) =>
        string.IsNullOrWhiteSpace(group.Label) ? "Group" : group.Label.Trim();
}
