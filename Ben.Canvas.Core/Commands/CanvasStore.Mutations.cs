using System.Text.Json;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Serialization;

namespace Ben.Canvas.Core.Commands;

public sealed partial class CanvasStore
{
    /// <summary>How far a duplicate or a repeated paste sits from the original.</summary>
    public const double CascadeOffset = 24;

    // ── Blocks ──────────────────────────────────────────────────────────

    /// <summary>Adds a block in front of everything, at least its minimum size. False when the board is full.</summary>
    public bool AddNode(CanvasNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Document.Nodes.Count >= MaxNodes) return false;
        if (Document.Nodes.Any(n => n.Id == node.Id)) return false;

        RaiseToMinimum(node);
        if (node.GroupId is { } gid && FindGroup(gid) is null) node.GroupId = null;
        node.Z = AllocateZ();
        return Execute(new AddNodesCommand(Document, [node], [], $"Add {Describe.Kind(node.Type)}"), CanvasChangeKind.Document);
    }

    /// <summary>Removes blocks with their connectors as one undo step. Locked blocks can be removed.</summary>
    public bool RemoveNodes(IEnumerable<Guid> ids)
    {
        var set = ids.Where(id => FindNode(id) is not null).ToHashSet();
        if (set.Count == 0) return false;
        return Execute(new RemoveNodesCommand(Document, set), CanvasChangeKind.Document);
    }

    /// <summary>Nudges blocks. Locked blocks stay where they are; false when nothing moved.</summary>
    public bool MoveNodes(IEnumerable<Guid> ids, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || (dx == 0 && dy == 0)) return false;
        var nodes = Resolve(ids).Where(n => !n.Locked).ToList();
        if (nodes.Count == 0) return false;
        return Execute(new MoveNodesCommand(nodes, dx, dy), CanvasChangeKind.NodeGeometry);
    }

    /// <summary>Sets a block's rectangle, holding axes it cannot resize on and raising it to its minimum.</summary>
    public bool ResizeNode(Guid id, double x, double y, double width, double height)
    {
        var node = FindNode(id);
        if (node is null || node.Locked) return false;
        var descriptor = BlockRegistry.Get(node.Type);

        if (!descriptor.ResizableWidth) { x = node.X; width = node.Width; }
        if (!descriptor.ResizableHeight) { y = node.Y; height = node.Height; }

        width = Math.Max(descriptor.MinWidth, Finite(width, node.Width));
        height = Math.Max(descriptor.MinHeight, Finite(height, node.Height));
        x = Finite(x, node.X);
        y = Finite(y, node.Y);

        var before = CanvasHitTester.RectOf(node);
        var after = new WorldRect(x, y, width, height);
        if (before == after) return false;

        return Execute(new ResizeNodeCommand(node, before, after), CanvasChangeKind.NodeGeometry);
    }

    public bool BringToFront(IEnumerable<Guid> ids)
    {
        var nodes = Resolve(ids).OrderBy(n => n.Z).ThenBy(n => n.Id).ToList();
        if (nodes.Count == 0) return false;
        var before = nodes.Select(n => n.Z).ToArray();
        var after = nodes.Select(_ => AllocateZ()).ToArray();
        return Execute(new SetZOrderCommand(nodes, before, after, "Bring to front"), CanvasChangeKind.Document);
    }

    public bool SendToBack(IEnumerable<Guid> ids)
    {
        var nodes = Resolve(ids).OrderBy(n => n.Z).ThenBy(n => n.Id).ToList();
        if (nodes.Count == 0) return false;
        var lowest = Document.Nodes.Min(n => n.Z);
        var before = nodes.Select(n => n.Z).ToArray();
        var after = nodes.Select((_, i) => lowest - nodes.Count + i).ToArray();
        return Execute(new SetZOrderCommand(nodes, before, after, "Send to back"), CanvasChangeKind.Document);
    }

    /// <summary>Edits a block's data through a copy; pushes nothing when the edit changed nothing.</summary>
    public bool UpdateNodeData(Guid id, Action<NodeData> mutate)
    {
        var node = FindNode(id);
        if (node is null) return false;
        var after = node.Data.Clone();
        mutate(after);
        return ReplaceNodeData(id, after);
    }

    /// <summary>Replaces a block's data whole - the single commit point for an in-block edit session.</summary>
    public bool ReplaceNodeData(Guid id, NodeData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var node = FindNode(id);
        if (node is null) return false;
        if (SameData(node.Data, data)) return false;
        return Execute(new UpdateNodeDataCommand(node, node.Data.Clone(), data.Clone()), CanvasChangeKind.NodeData);
    }

    /// <summary>
    /// Changes a block's data without an undo entry - for facts the server hands back (an upload id, a link
    /// preview) that a person did not do and should not undo.
    /// </summary>
    public bool SetNodeDataWithoutHistory(Guid id, Action<NodeData> mutate)
    {
        var node = FindNode(id);
        if (node is null) return false;
        mutate(node.Data);
        Notify(CanvasChangeKind.NodeData, [id]);
        return true;
    }

    public bool SetColor(IEnumerable<Guid> ids, string? colorKey)
    {
        if (colorKey is not null && !CanvasPalette.IsValid(colorKey)) return false;
        var nodes = Resolve(ids).ToList();
        if (nodes.Count == 0) return false;
        return Execute(new SetNodePropertyCommand<string?>(nodes, "Colour", n => n.ColorKey, (n, v) => n.ColorKey = v, colorKey), CanvasChangeKind.NodeData);
    }

    /// <summary>Whether a block wears its colour as an edge bar or as its whole background.</summary>
    public bool SetFill(IEnumerable<Guid> ids, NodeFill fill)
    {
        var nodes = Resolve(ids).ToList();
        if (nodes.Count == 0) return false;
        return Execute(new SetNodePropertyCommand<NodeFill>(
            nodes, fill == NodeFill.Solid ? "Fill" : "Unfill", n => n.Fill, (n, v) => n.Fill = v, fill),
            CanvasChangeKind.NodeData);
    }

    public bool SetLocked(IEnumerable<Guid> ids, bool locked)
    {
        var nodes = Resolve(ids).ToList();
        if (nodes.Count == 0) return false;
        return Execute(new SetNodePropertyCommand<bool>(nodes, locked ? "Lock" : "Unlock", n => n.Locked, (n, v) => n.Locked = v, locked), CanvasChangeKind.NodeData);
    }

    // ── Connectors ──────────────────────────────────────────────────────

    /// <summary>Connects two blocks. Null for a loop, a missing block or a connector that already exists.</summary>
    public CanvasEdge? Connect(Guid fromNodeId, Guid toNodeId, CanvasSide? fromSide = null, CanvasSide? toSide = null)
    {
        if (fromNodeId == toNodeId) return null;
        if (FindNode(fromNodeId) is null || FindNode(toNodeId) is null) return null;
        if (Document.Edges.Any(e => e.FromNodeId == fromNodeId && e.ToNodeId == toNodeId)) return null;

        var edge = new CanvasEdge { FromNodeId = fromNodeId, ToNodeId = toNodeId, FromSide = fromSide, ToSide = toSide };
        Execute(new AddEdgeCommand(Document, edge), CanvasChangeKind.Edges);
        return edge;
    }

    /// <summary>
    /// Adds a block beside an existing one and joins the two, as a single undo step.
    /// </summary>
    /// <param name="fromId">The block the new one grows out of.</param>
    /// <param name="side">Which side of it to grow from.</param>
    /// <param name="node">The new block, already carrying its type and data. Its position is set here.</param>
    /// <param name="at">
    /// Where to put the new block's centre, when somebody dragged to a spot and let go there. Null
    /// lets <see cref="BesidePlacement"/> choose — straight out from the side, clear of everything.
    /// </param>
    /// <returns>The new block and the connector, or null when nothing was added.</returns>
    /// <remarks>
    /// Ben, 2026-09-16: "Can we make it like Miro?" One step, not two, is the point: on those boards
    /// this is one gesture, so one Undo has to take back the whole of it. Two commands would leave a
    /// connector to a block that is no longer there — and then a second Undo to finish the job.
    /// </remarks>
    public (CanvasNode Node, CanvasEdge Edge)? AddConnected(Guid fromId, CanvasSide side, CanvasNode node, CanvasPoint? at = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (FindNode(fromId) is not { } from) return null;
        if (Document.Nodes.Count >= MaxNodes) return null;
        if (Document.Nodes.Any(n => n.Id == node.Id)) return null;

        RaiseToMinimum(node);

        var rect = at is { } point
            ? new WorldRect(point.X - node.Width / 2, point.Y - node.Height / 2, node.Width, node.Height)
            : BesidePlacement.For(
                CanvasHitTester.RectOf(from), side, node.Width, node.Height,
                Document.Nodes.Select(CanvasHitTester.RectOf));

        node.X = rect.X;
        node.Y = rect.Y;
        if (node.GroupId is { } gid && FindGroup(gid) is null) node.GroupId = null;
        node.Z = AllocateZ();

        var edge = new CanvasEdge
        {
            FromNodeId = fromId,
            ToNodeId = node.Id,
            FromSide = side,
            // A block dropped where somebody let go can end up anywhere, so let the drawing pick the
            // side that faces the source rather than insisting on the one opposite the gesture.
            ToSide = at is null ? BesidePlacement.Opposite(side) : null,
        };

        return Execute(new AddNodesCommand(Document, [node], [edge], $"Add {Describe.Kind(node.Type)}"), CanvasChangeKind.Document)
            ? (node, edge)
            : null;
    }

    public bool Disconnect(Guid edgeId)
    {
        var edge = FindEdge(edgeId);
        if (edge is null) return false;
        return Execute(new RemoveEdgeCommand(Document, edge), CanvasChangeKind.Edges);
    }

    public bool UpdateEdge(Guid edgeId, Action<CanvasEdge> mutate)
    {
        var edge = FindEdge(edgeId);
        if (edge is null) return false;
        var draft = edge.Clone();
        mutate(draft);
        if (draft.ColorKey is not null && !CanvasPalette.IsValid(draft.ColorKey)) draft.ColorKey = null;
        if (string.IsNullOrWhiteSpace(draft.Label)) draft.Label = null;

        var before = EdgeSnapshot.Of(edge);
        var after = EdgeSnapshot.Of(draft);
        if (before == after) return false;
        return Execute(new UpdateEdgeCommand(edge, before, after), CanvasChangeKind.Edges);
    }

    // ── Groups ──────────────────────────────────────────────────────────

    /// <summary>Groups blocks inside a rectangle around them, at least the group minimum.</summary>
    public CanvasGroup? Group(IEnumerable<Guid> ids, string label = "Group")
    {
        var members = Resolve(ids).ToList();
        if (members.Count == 0) return null;

        var bounds = WorldRect.Bounds(members.Select(CanvasHitTester.RectOf)).Inflate(BlockRegistry.GroupPadding);
        var width = Math.Max(BlockRegistry.GroupMinWidth, bounds.Width);
        var height = Math.Max(BlockRegistry.GroupMinHeight, bounds.Height);

        var group = new CanvasGroup
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Group" : label.Trim(),
            X = bounds.X - (width - bounds.Width) / 2,
            Y = bounds.Y - (height - bounds.Height) / 2,
            Width = width,
            Height = height,
            Z = AllocateZ(),
        };

        Execute(new AddGroupCommand(Document, group, members), CanvasChangeKind.Groups);
        return group;
    }

    /// <summary>Removes the group and keeps its blocks.</summary>
    public bool Ungroup(Guid groupId)
    {
        var group = FindGroup(groupId);
        if (group is null) return false;
        return Execute(new RemoveGroupCommand(Document, group), CanvasChangeKind.Groups);
    }

    /// <summary>Moves a group and its unlocked members as one undo step.</summary>
    public bool MoveGroup(Guid groupId, double dx, double dy)
    {
        var group = FindGroup(groupId);
        if (group is null || (dx == 0 && dy == 0) || !double.IsFinite(dx) || !double.IsFinite(dy)) return false;
        var members = MembersOf(groupId).Where(n => !n.Locked).ToList();
        List<IEditorCommand> parts = [new MoveGroupRectCommand(group, dx, dy)];
        if (members.Count > 0) parts.Add(new MoveNodesCommand(members, dx, dy));
        return Execute(new CompositeCommand("Move group", parts), CanvasChangeKind.Groups);
    }

    /// <summary>Changes the group's rectangle only; members stay where they are.</summary>
    public bool ResizeGroup(Guid groupId, double x, double y, double width, double height)
    {
        var group = FindGroup(groupId);
        if (group is null) return false;
        var before = CanvasHitTester.RectOf(group);
        var after = new WorldRect(
            Finite(x, group.X),
            Finite(y, group.Y),
            Math.Max(BlockRegistry.GroupMinWidth, Finite(width, group.Width)),
            Math.Max(BlockRegistry.GroupMinHeight, Finite(height, group.Height)));
        if (before == after) return false;
        return Execute(new ResizeGroupCommand(group, before, after), CanvasChangeKind.Groups);
    }

    public bool RenameGroup(Guid groupId, string? label)
    {
        var group = FindGroup(groupId);
        if (group is null) return false;
        var clean = string.IsNullOrWhiteSpace(label) ? "Group" : label.Trim();
        if (clean == group.Label) return false;
        return Execute(new SetGroupPropertyCommand<string>(group, "Rename group", g => g.Label, (g, v) => g.Label = v, clean), CanvasChangeKind.Groups);
    }

    /// <summary>Whether a group is a dashed outline or a solid titled panel.</summary>
    public bool SetGroupFill(Guid groupId, GroupFill fill)
    {
        var group = FindGroup(groupId);
        if (group is null || group.Fill == fill) return false;
        return Execute(new SetGroupPropertyCommand<GroupFill>(
            group, fill == GroupFill.Panel ? "Make a panel" : "Make an outline",
            g => g.Fill, (g, v) => g.Fill = v, fill), CanvasChangeKind.Groups);
    }

    public bool SetGroupColor(Guid groupId, string? colorKey)
    {
        var group = FindGroup(groupId);
        if (group is null || (colorKey is not null && !CanvasPalette.IsValid(colorKey)) || group.ColorKey == colorKey) return false;
        return Execute(new SetGroupPropertyCommand<string?>(group, "Change group colour", g => g.ColorKey, (g, v) => g.ColorKey = v, colorKey), CanvasChangeKind.Groups);
    }

    public bool AssignGroup(Guid nodeId, Guid? groupId)
    {
        var node = FindNode(nodeId);
        if (node is null || node.GroupId == groupId) return false;
        if (groupId is { } gid && FindGroup(gid) is null) return false;
        return Execute(new AssignGroupCommand(node, node.GroupId, groupId), CanvasChangeKind.Groups);
    }

    // ── Paste and duplicate ─────────────────────────────────────────────

    /// <summary>
    /// Adds blocks and the connectors between them as one undo step, in list order at the front.
    /// False when the board would pass its block limit, so nothing half-pastes.
    /// </summary>
    public bool PasteMany(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges) =>
        AddMany(nodes, edges, $"Paste {Describe.Items(nodes.Count)}");

    /// <summary>Copies blocks 24 px down and right with new ids; connectors between them are copied too.</summary>
    public IReadOnlyList<Guid> Duplicate(IEnumerable<Guid> ids)
    {
        var sources = Resolve(ids).OrderBy(n => n.Z).ThenBy(n => n.Id).ToList();
        if (sources.Count == 0) return [];

        var map = new Dictionary<Guid, Guid>();
        var copies = sources.Select(source =>
        {
            var copy = source.Clone();
            copy.Id = Guid.NewGuid();
            copy.X += CascadeOffset;
            copy.Y += CascadeOffset;
            map[source.Id] = copy.Id;
            return copy;
        }).ToList();

        var edges = Document.Edges
            .Where(e => map.ContainsKey(e.FromNodeId) && map.ContainsKey(e.ToNodeId))
            .Select(e =>
            {
                var copy = e.Clone();
                copy.Id = Guid.NewGuid();
                copy.FromNodeId = map[e.FromNodeId];
                copy.ToNodeId = map[e.ToNodeId];
                return copy;
            })
            .ToList();

        return AddMany(copies, edges, "Duplicate") ? copies.Select(c => c.Id).ToList() : [];
    }

    private bool AddMany(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges, string description)
    {
        if (nodes.Count == 0) return false;
        if (Document.Nodes.Count + nodes.Count > MaxNodes) return false;

        var existing = Document.Nodes.Select(n => n.Id).ToHashSet();
        if (nodes.Any(n => existing.Contains(n.Id)) || nodes.Select(n => n.Id).Distinct().Count() != nodes.Count) return false;

        foreach (var node in nodes)
        {
            RaiseToMinimum(node);
            if (node.GroupId is { } gid && FindGroup(gid) is null) node.GroupId = null;
            node.Z = AllocateZ();
        }

        var ids = existing.Concat(nodes.Select(n => n.Id)).ToHashSet();
        var keptEdges = edges
            .Where(e => e.FromNodeId != e.ToNodeId && ids.Contains(e.FromNodeId) && ids.Contains(e.ToNodeId))
            .ToList();

        return Execute(new AddNodesCommand(Document, nodes, keptEdges, description), CanvasChangeKind.Document);
    }

    // ── Live gestures ───────────────────────────────────────────────────

    /// <summary>
    /// Starts a drag. Blocks move freely while it runs and one command is recorded when it commits.
    /// </summary>
    /// <remarks>
    /// Starting a second drag cancels the first, because a pointer that was cancelled by the browser may
    /// never have told C# it ended. Locked blocks are left behind; members of a listed group come along.
    /// </remarks>
    public LiveMoveSession BeginMove(IReadOnlyList<Guid> nodeIds, IReadOnlyList<Guid> groupIds, SnapGuides guides, double thresholdWorld, bool snapEnabled, double gridSize = 0)
    {
        CancelLiveSessions();

        var groups = groupIds.Select(FindGroup).OfType<CanvasGroup>().Distinct().ToList();
        var groupSet = groups.Select(g => g.Id).ToHashSet();
        var nodes = Resolve(nodeIds)
            .Concat(Document.Nodes.Where(n => n.GroupId is { } gid && groupSet.Contains(gid)))
            .Where(n => !n.Locked)
            .Distinct()
            .ToList();

        var session = new LiveMoveSession(this, nodes, groups, guides ?? SnapGuides.Empty, thresholdWorld, snapEnabled, gridSize);
        _activeMove = session;
        return session;
    }

    /// <summary>Starts a resize of a block from a handle; null when the block cannot be resized that way.</summary>
    public LiveResizeSession? BeginResize(Guid nodeId, string handle)
    {
        CancelLiveSessions();
        var node = FindNode(nodeId);
        if (node is null || node.Locked) return null;
        var descriptor = BlockRegistry.Get(node.Type);
        if (!RectResizeMath.AllowedFor(handle, descriptor.ResizableWidth, descriptor.ResizableHeight)) return null;

        var session = LiveResizeSession.ForNode(this, node, handle, descriptor.MinWidth, descriptor.MinHeight);
        _activeResize = session;
        return session;
    }

    public LiveResizeSession? BeginResizeGroup(Guid groupId, string handle)
    {
        CancelLiveSessions();
        var group = FindGroup(groupId);
        if (group is null || !RectResizeMath.AllowedFor(handle, true, true)) return null;
        var session = LiveResizeSession.ForGroup(this, group, handle);
        _activeResize = session;
        return session;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private IEnumerable<CanvasNode> Resolve(IEnumerable<Guid> ids)
    {
        var set = ids as IReadOnlySet<Guid> ?? ids.ToHashSet();
        return Document.Nodes.Where(n => set.Contains(n.Id));
    }

    private static void RaiseToMinimum(CanvasNode node)
    {
        var descriptor = BlockRegistry.Get(node.Type);
        if (!double.IsFinite(node.Width) || node.Width <= 0) node.Width = descriptor.DefaultWidth;
        if (!double.IsFinite(node.Height) || node.Height <= 0) node.Height = descriptor.DefaultHeight;
        node.Width = Math.Max(node.Width, descriptor.MinWidth);
        node.Height = Math.Max(node.Height, descriptor.MinHeight);
    }

    private static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private static bool SameData(NodeData a, NodeData b) =>
        a.GetType() == b.GetType() &&
        JsonSerializer.Serialize(a, CanvasSerializer.CompactOptions) == JsonSerializer.Serialize(b, CanvasSerializer.CompactOptions);
}
