using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// What is selected, focused and being edited on the board.
/// </summary>
/// <remarks>
/// A block and a connector are never selected together: choosing one clears the other, so Delete always
/// means one thing. <see cref="Prune"/> runs after every document change, so undoing an add never leaves a
/// selection pointing at a block that is no longer there.
/// </remarks>
public sealed class SelectionState
{
    private readonly HashSet<Guid> _nodes = [];

    public IReadOnlySet<Guid> NodeIds => _nodes;
    public Guid? EdgeId { get; private set; }
    public Guid? GroupId { get; private set; }

    /// <summary>The block that owns the roving tab stop.</summary>
    public Guid? FocusedNodeId { get; set; }

    /// <summary>The block whose contents are open for editing.</summary>
    public Guid? EditingNodeId { get; private set; }

    /// <summary>The group whose label is being renamed.</summary>
    public Guid? EditingGroupId { get; private set; }

    /// <summary>Touch connect mode: ports show on the selected block.</summary>
    public bool ConnectMode { get; private set; }

    public event Action? OnChanged;

    public bool IsSelected(Guid nodeId) => _nodes.Contains(nodeId);

    public void Select(Guid nodeId, bool additive = false)
    {
        if (!additive && _nodes.Count == 1 && _nodes.Contains(nodeId) && EdgeId is null && GroupId is null) return;
        if (!additive) _nodes.Clear();
        _nodes.Add(nodeId);
        EdgeId = null;
        GroupId = null;
        FocusedNodeId = nodeId;
        EndEditIfOther(nodeId);
        Changed();
    }

    public void Toggle(Guid nodeId)
    {
        if (!_nodes.Remove(nodeId)) _nodes.Add(nodeId);
        EdgeId = null;
        GroupId = null;
        Changed();
    }

    public void SelectMany(IEnumerable<Guid> nodeIds, bool additive = false)
    {
        var ids = nodeIds.ToList();
        if (!additive && _nodes.SetEquals(ids) && EdgeId is null && GroupId is null) return;
        if (!additive) _nodes.Clear();
        foreach (var id in ids) _nodes.Add(id);
        EdgeId = null;
        GroupId = null;
        EditingNodeId = null;
        Changed();
    }

    public void SelectEdge(Guid edgeId)
    {
        if (EdgeId == edgeId && _nodes.Count == 0) return;
        _nodes.Clear();
        GroupId = null;
        EdgeId = edgeId;
        EditingNodeId = null;
        Changed();
    }

    public void SelectGroup(Guid groupId)
    {
        if (GroupId == groupId && _nodes.Count == 0) return;
        _nodes.Clear();
        EdgeId = null;
        GroupId = groupId;
        EditingNodeId = null;
        Changed();
    }

    public void Clear()
    {
        if (_nodes.Count == 0 && EdgeId is null && GroupId is null && EditingNodeId is null && EditingGroupId is null && !ConnectMode) return;
        _nodes.Clear();
        EdgeId = null;
        GroupId = null;
        EditingNodeId = null;
        EditingGroupId = null;
        ConnectMode = false;
        Changed();
    }

    public void BeginEdit(Guid nodeId)
    {
        if (!_nodes.Contains(nodeId) || _nodes.Count != 1)
        {
            _nodes.Clear();
            _nodes.Add(nodeId);
        }

        EdgeId = null;
        GroupId = null;
        FocusedNodeId = nodeId;
        EditingNodeId = nodeId;
        Changed();
    }

    public void EndEdit()
    {
        if (EditingNodeId is null && EditingGroupId is null) return;
        EditingNodeId = null;
        EditingGroupId = null;
        Changed();
    }

    public void BeginRenameGroup(Guid groupId)
    {
        SelectGroup(groupId);
        EditingGroupId = groupId;
        Changed();
    }

    public void SetConnectMode(bool on)
    {
        if (ConnectMode == on) return;
        ConnectMode = on;
        Changed();
    }

    /// <summary>Drops ids the document no longer has.</summary>
    public void Prune(CanvasDocument document)
    {
        var nodeIds = document.Nodes.Select(n => n.Id).ToHashSet();
        var changed = _nodes.RemoveWhere(id => !nodeIds.Contains(id)) > 0;

        if (EdgeId is { } edge && document.Edges.All(e => e.Id != edge)) { EdgeId = null; changed = true; }
        if (GroupId is { } group && document.Groups.All(g => g.Id != group)) { GroupId = null; EditingGroupId = null; changed = true; }
        if (EditingNodeId is { } editing && !nodeIds.Contains(editing)) { EditingNodeId = null; changed = true; }
        if (FocusedNodeId is { } focused && !nodeIds.Contains(focused)) FocusedNodeId = null;

        if (changed) Changed();
    }

    private void EndEditIfOther(Guid nodeId)
    {
        if (EditingNodeId is { } editing && editing != nodeId) EditingNodeId = null;
    }

    private void Changed() => OnChanged?.Invoke();
}

/// <summary>
/// The board's camera as C# knows it.
/// </summary>
/// <remarks>
/// The gesture script owns the camera while a pan or pinch runs and reports where it ended. C# changes it
/// only for buttons, keys and fit, and pushes the result back to the script.
/// </remarks>
public sealed class CanvasViewportState
{
    public CanvasViewport Current { get; private set; } = CanvasViewport.Identity;

    public double BoardWidth { get; private set; } = 1280;
    public double BoardHeight { get; private set; } = 800;

    /// <summary>True between the start and end of a block drag or resize; the board does not render meanwhile.</summary>
    public bool GestureActive { get; private set; }

    public event Action? OnChanged;

    public void Set(CanvasViewport viewport)
    {
        var clamped = ViewportMath.Clamp(viewport);
        if (clamped == Current) return;
        Current = clamped;
        OnChanged?.Invoke();
    }

    public void SetBoardSize(double width, double height)
    {
        if (width <= 0 || height <= 0 || (width == BoardWidth && height == BoardHeight)) return;
        BoardWidth = width;
        BoardHeight = height;
        OnChanged?.Invoke();
    }

    public void SetGestureActive(bool active)
    {
        if (GestureActive == active) return;
        GestureActive = active;
        if (!active) OnChanged?.Invoke();
    }

    public void ZoomAboutCentre(double factor) =>
        Set(ViewportMath.ZoomAboutPoint(Current, BoardWidth / 2, BoardHeight / 2, factor));

    /// <summary>Zoom 100% keeping the world point at the centre of the board where it is.</summary>
    public void ResetZoom()
    {
        var centre = WorldCentre();
        Set(new CanvasViewport(BoardWidth / 2 - centre.X, BoardHeight / 2 - centre.Y, 1));
    }

    public void Fit(WorldRect bounds) => Set(ViewportMath.FitToContent(bounds, BoardWidth, BoardHeight));

    public CanvasPoint WorldCentre() => ViewportMath.ClientToWorld(Current, BoardWidth / 2, BoardHeight / 2);

    public WorldRect VisibleWorldRect() => ViewportMath.VisibleWorldRect(Current, BoardWidth, BoardHeight);

    /// <summary>Centres the view on a world point without changing zoom.</summary>
    public void CentreOn(double worldX, double worldY) =>
        Set(Current with { PanX = BoardWidth / 2 - worldX * Current.Zoom, PanY = BoardHeight / 2 - worldY * Current.Zoom });
}

/// <summary>
/// What the board says to screen readers.
/// </summary>
/// <remarks>
/// A live region only speaks when its text changes, so saying the same sentence twice ("Moved to 10, 20"
/// after two identical nudges) toggles an invisible zero-width space on the end.
/// </remarks>
public sealed class AnnouncerService
{
    private const char Nudge = '​';

    public string Text { get; private set; } = "";

    public event Action? OnChanged;

    public void Say(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Text = Text.TrimEnd(Nudge) == text && !Text.EndsWith(Nudge) ? text + Nudge : text;
        OnChanged?.Invoke();
    }
}
