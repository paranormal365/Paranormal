using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Commands;

/// <summary>Where a drag ended up after snapping, and which guide lines to draw.</summary>
public readonly record struct LiveMoveResult(double SnappedDx, double SnappedDy, double? GuideX, double? GuideY);

/// <summary>
/// One drag of blocks and groups.
/// </summary>
/// <remarks>
/// <para>The board's script moves pixels during a drag and tells C# once, at the end: Begin, Move with the
/// final raw delta, Commit. C# re-snaps there, so what is stored never trusts what the script previewed.
/// Keyboard move mode may call <see cref="Move"/> many times; each call is a delta from where the drag
/// started, not from the last call.</para>
///
/// <para>Commit records one undo entry holding the move and any change of group membership, decided by where
/// each block's centre landed - so a single undo puts a block back where it was and back in its old group.
/// </para>
/// </remarks>
public sealed class LiveMoveSession
{
    private readonly CanvasStore _store;
    private readonly List<(CanvasNode Node, double X, double Y)> _nodes;
    private readonly List<(CanvasGroup Group, double X, double Y)> _groups;
    private readonly SnapGuides _guides;
    private readonly double _threshold;
    private readonly bool _snap;
    private readonly double _grid;
    private double _dx;
    private double _dy;

    internal LiveMoveSession(CanvasStore store, List<CanvasNode> nodes, List<CanvasGroup> groups, SnapGuides guides, double threshold, bool snap, double grid)
    {
        _store = store;
        _nodes = nodes.Select(n => (n, n.X, n.Y)).ToList();
        _groups = groups.Select(g => (g, g.X, g.Y)).ToList();
        _guides = guides;
        _threshold = threshold;
        _snap = snap;
        _grid = grid;
    }

    public bool IsActive { get; private set; } = true;

    public IReadOnlyList<Guid> NodeIds => _nodes.Select(n => n.Node.Id).ToList();

    public IReadOnlyList<Guid> GroupIds => _groups.Select(g => g.Group.Id).ToList();

    public LiveMoveResult Move(double dx, double dy)
    {
        if (!IsActive || !double.IsFinite(dx) || !double.IsFinite(dy)) return new(_dx, _dy, null, null);

        WorldRect? primary = _nodes.Count > 0
            ? new WorldRect(_nodes[0].X, _nodes[0].Y, _nodes[0].Node.Width, _nodes[0].Node.Height)
            : _groups.Count > 0 ? new WorldRect(_groups[0].X, _groups[0].Y, _groups[0].Group.Width, _groups[0].Group.Height) : null;

        double? guideX = null, guideY = null;
        var sdx = dx;
        var sdy = dy;

        if (primary is { } p)
        {
            (sdx, guideX) = SnapAxis(p.X, p.Width, dx, _guides.X);
            (sdy, guideY) = SnapAxis(p.Y, p.Height, dy, _guides.Y);
        }

        _dx = sdx;
        _dy = sdy;
        foreach (var (node, x, y) in _nodes) { node.X = x + sdx; node.Y = y + sdy; }
        foreach (var (group, x, y) in _groups) { group.X = x + sdx; group.Y = y + sdy; }

        _store.Notify(CanvasChangeKind.NodeGeometry, NodeIds);
        return new LiveMoveResult(sdx, sdy, guideX, guideY);
    }

    private (double Delta, double? Guide) SnapAxis(double origin, double size, double delta, IReadOnlyList<double> guides)
    {
        var raw = origin + delta;
        if (_snap && guides.Count > 0)
        {
            var guide = CanvasSnapCalculator.ActiveGuide(raw, size, guides, _threshold);
            if (guide is not null) return (CanvasSnapCalculator.Snap(raw, size, guides, _threshold) - origin, guide);
        }

        if (_grid > 0) return (SnapGuideCollector.GridSnap(raw, _grid) - origin, null);
        return (delta, null);
    }

    /// <summary>Records the drag as one undo entry. False, and nothing recorded, when nothing moved.</summary>
    public bool Commit()
    {
        if (!IsActive) return false;
        IsActive = false;
        _store.EndSession(this);

        if (Math.Abs(_dx) <= 1e-9 && Math.Abs(_dy) <= 1e-9) return false;

        List<IEditorCommand> parts = [];
        if (_nodes.Count > 0) parts.Add(new MoveNodesCommand(_nodes.Select(n => n.Node).ToList(), _dx, _dy));
        foreach (var (group, _, _) in _groups) parts.Add(new MoveGroupRectCommand(group, _dx, _dy));

        var moving = _groups.Select(g => g.Group.Id).ToHashSet();
        var assignments = 0;
        foreach (var (node, _, _) in _nodes)
        {
            if (node.GroupId is { } own && moving.Contains(own)) continue;
            var target = GroupMembership.GroupContaining(_store.Document.Groups, CanvasHitTester.RectOf(node), moving);
            if (target == node.GroupId) continue;

            var assign = new AssignGroupCommand(node, node.GroupId, target);
            assign.Execute();
            parts.Add(assign);
            assignments++;
        }

        var description = _nodes.Count > 0 ? $"Move {Describe.Items(_nodes.Count)}" : "Move group";
        var kind = _groups.Count > 0 || assignments > 0 ? CanvasChangeKind.Groups : CanvasChangeKind.NodeGeometry;
        _store.Record(new CompositeCommand(description, parts), kind);
        return true;
    }

    /// <summary>Puts everything back where the drag started, recording nothing.</summary>
    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;
        _store.EndSession(this);
        foreach (var (node, x, y) in _nodes) { node.X = x; node.Y = y; }
        foreach (var (group, x, y) in _groups) { group.X = x; group.Y = y; }
        _dx = _dy = 0;
        _store.Notify(CanvasChangeKind.NodeGeometry, NodeIds);
    }
}

/// <summary>One resize of a block or a group from a handle.</summary>
public sealed class LiveResizeSession
{
    private readonly CanvasStore _store;
    private readonly WorldRect _original;
    private readonly string _handle;
    private readonly double _minWidth;
    private readonly double _minHeight;
    private readonly Func<WorldRect> _read;
    private readonly Action<WorldRect> _write;
    private readonly Func<WorldRect, WorldRect, IEditorCommand> _command;
    private readonly Guid? _nodeId;
    private readonly CanvasChangeKind _kind;

    private LiveResizeSession(CanvasStore store, string handle, double minWidth, double minHeight, Func<WorldRect> read, Action<WorldRect> write,
        Func<WorldRect, WorldRect, IEditorCommand> command, Guid? nodeId, CanvasChangeKind kind)
    {
        _store = store;
        _handle = handle;
        _minWidth = minWidth;
        _minHeight = minHeight;
        _read = read;
        _write = write;
        _command = command;
        _nodeId = nodeId;
        _kind = kind;
        _original = read();
    }

    internal static LiveResizeSession ForNode(CanvasStore store, CanvasNode node, string handle, double minWidth, double minHeight) =>
        new(store, handle, minWidth, minHeight,
            () => CanvasHitTester.RectOf(node),
            r => { node.X = r.X; node.Y = r.Y; node.Width = r.Width; node.Height = r.Height; },
            (before, after) => new ResizeNodeCommand(node, before, after),
            node.Id, CanvasChangeKind.NodeGeometry);

    internal static LiveResizeSession ForGroup(CanvasStore store, CanvasGroup group, string handle) =>
        new(store, handle, BlockRegistry.GroupMinWidth, BlockRegistry.GroupMinHeight,
            () => CanvasHitTester.RectOf(group),
            r => { group.X = r.X; group.Y = r.Y; group.Width = r.Width; group.Height = r.Height; },
            (before, after) => new ResizeGroupCommand(group, before, after),
            null, CanvasChangeKind.Groups);

    public bool IsActive { get; private set; } = true;

    public WorldRect Original => _original;

    /// <summary>Applies a delta from where the resize started; returns the new rectangle.</summary>
    public WorldRect Move(double dx, double dy, bool keepAspect)
    {
        if (!IsActive || !double.IsFinite(dx) || !double.IsFinite(dy)) return _read();
        var rect = RectResizeMath.ApplyResize(_original, _handle, dx, dy, _minWidth, _minHeight, keepAspect);
        _write(rect);
        _store.Notify(_kind, _nodeId is { } id ? [id] : []);
        return rect;
    }

    public bool Commit()
    {
        if (!IsActive) return false;
        IsActive = false;
        _store.EndSession(this);
        var current = _read();
        if (current == _original) return false;
        _store.Record(_command(_original, current), _kind);
        return true;
    }

    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;
        _store.EndSession(this);
        _write(_original);
        _store.Notify(_kind, _nodeId is { } id ? [id] : []);
    }
}
