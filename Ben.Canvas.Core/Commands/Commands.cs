using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Commands;

// Every command captures the model objects it changes when it is built and finds them again by Id on
// undo, the same shape as the video editor's AddClipCommand. Descriptions are read by people.

internal static class Describe
{
    public static string Items(int n) => n == 1 ? "1 item" : $"{n} items";

    public static string Kind(CanvasNodeType type) => Blocks.BlockRegistry.Get(type).DisplayName.ToLowerInvariant();
}

internal sealed class AddNodesCommand(CanvasDocument document, IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges, string description)
    : IEditorCommand, ITouchesNodes, IHoldsData
{
    public string Description { get; } = description;

    public void Execute()
    {
        foreach (var node in nodes)
            if (!document.Nodes.Any(n => n.Id == node.Id)) document.Nodes.Add(node);
        foreach (var edge in edges)
            if (!document.Edges.Any(e => e.Id == edge.Id)) document.Edges.Add(edge);
    }

    public void Undo()
    {
        var edgeIds = edges.Select(e => e.Id).ToHashSet();
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        document.Edges.RemoveAll(e => edgeIds.Contains(e.Id));
        document.Nodes.RemoveAll(n => nodeIds.Contains(n.Id));
    }

    public IEnumerable<Guid> NodeIds => nodes.Select(n => n.Id);
    public IEnumerable<NodeData> HeldData => nodes.Select(n => n.Data);
}

/// <summary>Removes blocks and every connector attached to them; undo puts both back where they were.</summary>
internal sealed class RemoveNodesCommand : IEditorCommand, ITouchesNodes, IHoldsData
{
    private readonly CanvasDocument _document;
    private readonly List<(int Index, CanvasNode Node)> _nodes;
    private readonly List<(int Index, CanvasEdge Edge)> _edges;

    public RemoveNodesCommand(CanvasDocument document, IReadOnlySet<Guid> ids)
    {
        _document = document;
        _nodes = document.Nodes.Select((n, i) => (i, n)).Where(p => ids.Contains(p.n.Id)).ToList();
        _edges = document.Edges.Select((e, i) => (i, e)).Where(p => ids.Contains(p.e.FromNodeId) || ids.Contains(p.e.ToNodeId)).ToList();
        Description = _nodes.Count == 1 ? $"Remove {Describe.Kind(_nodes[0].Node.Type)}" : $"Remove {Describe.Items(_nodes.Count)}";
    }

    public string Description { get; }
    public int Count => _nodes.Count;

    public void Execute()
    {
        var edgeIds = _edges.Select(e => e.Edge.Id).ToHashSet();
        var nodeIds = _nodes.Select(n => n.Node.Id).ToHashSet();
        _document.Edges.RemoveAll(e => edgeIds.Contains(e.Id));
        _document.Nodes.RemoveAll(n => nodeIds.Contains(n.Id));
    }

    public void Undo()
    {
        foreach (var (index, node) in _nodes.OrderBy(p => p.Index))
            if (!_document.Nodes.Any(n => n.Id == node.Id))
                _document.Nodes.Insert(Math.Min(index, _document.Nodes.Count), node);
        foreach (var (index, edge) in _edges.OrderBy(p => p.Index))
            if (!_document.Edges.Any(e => e.Id == edge.Id))
                _document.Edges.Insert(Math.Min(index, _document.Edges.Count), edge);
    }

    public IEnumerable<Guid> NodeIds => _nodes.Select(n => n.Node.Id);
    public IEnumerable<NodeData> HeldData => _nodes.Select(n => n.Node.Data);
}

internal sealed class MoveNodesCommand(IReadOnlyList<CanvasNode> nodes, double dx, double dy) : IEditorCommand, ITouchesNodes
{
    public string Description { get; } = $"Move {Describe.Items(nodes.Count)}";

    public void Execute()
    {
        foreach (var node in nodes) { node.X += dx; node.Y += dy; }
    }

    public void Undo()
    {
        foreach (var node in nodes) { node.X -= dx; node.Y -= dy; }
    }

    public IEnumerable<Guid> NodeIds => nodes.Select(n => n.Id);
}

internal sealed class ResizeNodeCommand(CanvasNode node, WorldRect before, WorldRect after) : IEditorCommand, ITouchesNodes
{
    public string Description => "Resize";
    public void Execute() => Apply(after);
    public void Undo() => Apply(before);

    private void Apply(WorldRect r)
    {
        node.X = r.X; node.Y = r.Y; node.Width = r.Width; node.Height = r.Height;
    }

    public IEnumerable<Guid> NodeIds => [node.Id];
}

internal sealed class SetZOrderCommand(IReadOnlyList<CanvasNode> nodes, int[] before, int[] after, string description) : IEditorCommand, ITouchesNodes
{
    public string Description { get; } = description;

    public void Execute()
    {
        for (var i = 0; i < nodes.Count; i++) nodes[i].Z = after[i];
    }

    public void Undo()
    {
        for (var i = 0; i < nodes.Count; i++) nodes[i].Z = before[i];
    }

    public IEnumerable<Guid> NodeIds => nodes.Select(n => n.Id);
}

/// <param name="sizeBefore">
/// The block's rectangle when the edit began, and <paramref name="sizeAfter"/> the one it ended with.
/// Both null for an edit that changed nothing but the content, which is almost all of them.
/// </param>
/// <remarks>
/// <b>Why an edit carries a rectangle.</b> A grid grows itself as rows are added, because a button in
/// a block's own toolbar has to produce a row somebody can see. That growth is a consequence of the
/// edit rather than a second thing the person did, so it belongs in the SAME undo step — otherwise one
/// Ctrl+Z leaves a two-row table in a block sized for five, and a second is needed to finish the job.
/// </remarks>
internal sealed class UpdateNodeDataCommand(
    CanvasNode node, NodeData before, NodeData after,
    WorldRect? sizeBefore = null, WorldRect? sizeAfter = null)
    : IEditorCommand, ITouchesNodes, IHoldsData
{
    public string Description { get; } = $"Edit {Describe.Kind(node.Type)}";

    // Clones on the way in and out, so a later edit to the live data can never rewrite what undo restores.
    public void Execute()
    {
        node.Data = after.Clone();
        Apply(sizeAfter);
    }

    public void Undo()
    {
        node.Data = before.Clone();
        Apply(sizeBefore);
    }

    private void Apply(WorldRect? rect)
    {
        if (rect is not { } r) return;
        node.X = r.X;
        node.Y = r.Y;
        node.Width = r.Width;
        node.Height = r.Height;
    }

    public IEnumerable<Guid> NodeIds => [node.Id];
    public IEnumerable<NodeData> HeldData => [before, after];
}

internal sealed class SetNodePropertyCommand<T>(IReadOnlyList<CanvasNode> nodes, string description, Func<CanvasNode, T> get, Action<CanvasNode, T> set, T value)
    : IEditorCommand, ITouchesNodes
{
    private readonly T[] _before = nodes.Select(get).ToArray();

    public string Description { get; } = description;

    public void Execute()
    {
        foreach (var node in nodes) set(node, value);
    }

    public void Undo()
    {
        for (var i = 0; i < nodes.Count; i++) set(nodes[i], _before[i]);
    }

    public IEnumerable<Guid> NodeIds => nodes.Select(n => n.Id);
}

internal sealed class AddEdgeCommand(CanvasDocument document, CanvasEdge edge) : IEditorCommand
{
    public string Description => "Connect";

    public void Execute()
    {
        if (!document.Edges.Any(e => e.Id == edge.Id)) document.Edges.Add(edge);
    }

    public void Undo() => document.Edges.RemoveAll(e => e.Id == edge.Id);
}

internal sealed class RemoveEdgeCommand(CanvasDocument document, CanvasEdge edge) : IEditorCommand
{
    private readonly int _index = document.Edges.IndexOf(edge);

    public string Description => "Disconnect";

    public void Execute() => document.Edges.RemoveAll(e => e.Id == edge.Id);

    public void Undo()
    {
        if (!document.Edges.Any(e => e.Id == edge.Id))
            document.Edges.Insert(Math.Clamp(_index, 0, document.Edges.Count), edge);
    }
}

/// <summary>The editable parts of a connector, captured whole so undo restores every one.</summary>
/// <summary>
/// Everything about a connector that an edit can change, for the undo stack and for the
/// did-anything-actually-change test.
/// </summary>
/// <remarks>
/// EVERY editable field has to be here. The list is hand-written, and a field missing from it fails
/// twice over: <c>CanvasStore.UpdateEdge</c> compares before against after and rejects the edit as a
/// no-op, so the change never happens at all — and were it to happen, undo would not take it back.
/// That is exactly how M9's route, line, markers and icon first behaved: every select in the
/// properties panel did nothing, silently.
/// </remarks>
internal readonly record struct EdgeSnapshot(
    CanvasSide? FromSide, CanvasSide? ToSide, string? Label, EdgeArrow Arrow, string? ColorKey,
    EdgeMarker? FromMarker, EdgeMarker? ToMarker, EdgeLine Line, EdgeRoute Route, string? Icon)
{
    public static EdgeSnapshot Of(CanvasEdge e) => new(
        e.FromSide, e.ToSide, e.Label, e.Arrow, e.ColorKey,
        e.FromMarker, e.ToMarker, e.Line, e.Route, e.Icon);

    public void ApplyTo(CanvasEdge e)
    {
        e.FromSide = FromSide; e.ToSide = ToSide; e.Label = Label; e.Arrow = Arrow; e.ColorKey = ColorKey;
        e.FromMarker = FromMarker; e.ToMarker = ToMarker; e.Line = Line; e.Route = Route; e.Icon = Icon;
    }
}

internal sealed class UpdateEdgeCommand(CanvasEdge edge, EdgeSnapshot before, EdgeSnapshot after) : IEditorCommand
{
    public string Description => "Edit connector";
    public void Execute() => after.ApplyTo(edge);
    public void Undo() => before.ApplyTo(edge);
}

internal sealed class AddGroupCommand : IEditorCommand, ITouchesNodes
{
    private readonly CanvasDocument _document;
    private readonly CanvasGroup _group;
    private readonly IReadOnlyList<CanvasNode> _members;
    private readonly Guid?[] _previous;

    public AddGroupCommand(CanvasDocument document, CanvasGroup group, IReadOnlyList<CanvasNode> members)
    {
        _document = document;
        _group = group;
        _members = members;
        _previous = members.Select(m => m.GroupId).ToArray();
    }

    public string Description => "Group";

    public void Execute()
    {
        if (!_document.Groups.Any(g => g.Id == _group.Id)) _document.Groups.Add(_group);
        foreach (var member in _members) member.GroupId = _group.Id;
    }

    public void Undo()
    {
        for (var i = 0; i < _members.Count; i++) _members[i].GroupId = _previous[i];
        _document.Groups.RemoveAll(g => g.Id == _group.Id);
    }

    public IEnumerable<Guid> NodeIds => _members.Select(m => m.Id);
}

internal sealed class RemoveGroupCommand : IEditorCommand, ITouchesNodes
{
    private readonly CanvasDocument _document;
    private readonly CanvasGroup _group;
    private readonly int _index;
    private readonly IReadOnlyList<CanvasNode> _members;

    public RemoveGroupCommand(CanvasDocument document, CanvasGroup group)
    {
        _document = document;
        _group = group;
        _index = document.Groups.IndexOf(group);
        _members = document.Nodes.Where(n => n.GroupId == group.Id).ToList();
    }

    public string Description => "Ungroup";

    public void Execute()
    {
        foreach (var member in _members) member.GroupId = null;
        _document.Groups.RemoveAll(g => g.Id == _group.Id);
    }

    public void Undo()
    {
        if (!_document.Groups.Any(g => g.Id == _group.Id))
            _document.Groups.Insert(Math.Clamp(_index, 0, _document.Groups.Count), _group);
        foreach (var member in _members) member.GroupId = _group.Id;
    }

    public IEnumerable<Guid> NodeIds => _members.Select(m => m.Id);
}

internal sealed class MoveGroupRectCommand(CanvasGroup group, double dx, double dy) : IEditorCommand
{
    public string Description => "Move group";

    public void Execute() { group.X += dx; group.Y += dy; }

    public void Undo() { group.X -= dx; group.Y -= dy; }
}

internal sealed class ResizeGroupCommand(CanvasGroup group, WorldRect before, WorldRect after) : IEditorCommand
{
    public string Description => "Resize group";
    public void Execute() => Apply(after);
    public void Undo() => Apply(before);

    private void Apply(WorldRect r)
    {
        group.X = r.X; group.Y = r.Y; group.Width = r.Width; group.Height = r.Height;
    }
}

internal sealed class SetGroupPropertyCommand<T>(CanvasGroup group, string description, Func<CanvasGroup, T> get, Action<CanvasGroup, T> set, T value) : IEditorCommand
{
    private readonly T _before = get(group);

    public string Description { get; } = description;
    public void Execute() => set(group, value);
    public void Undo() => set(group, _before);
}

internal sealed class AssignGroupCommand(CanvasNode node, Guid? before, Guid? after) : IEditorCommand, ITouchesNodes
{
    public string Description => after is null ? "Move out of group" : "Move into group";
    public void Execute() => node.GroupId = after;
    public void Undo() => node.GroupId = before;
    public IEnumerable<Guid> NodeIds => [node.Id];
}
