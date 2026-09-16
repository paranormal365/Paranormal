using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;

namespace Ben.Canvas.Core.Commands;

/// <summary>
/// The open board, its undo history, and the one place the board changes.
/// </summary>
/// <remarks>
/// <para>Every change goes through a command the store runs, so every change can be undone and every change
/// raises <see cref="OnChange"/>. Nothing else mutates <see cref="Document"/> - except a live drag, which
/// moves blocks freely while the pointer is down and records one command when it ends.</para>
///
/// <para><see cref="Versions"/> is the render gate. A block re-renders only when its number moves, so the
/// number is bumped for every block a change names - on execute, undo, redo, live moves, commit and cancel.
/// A block whose number did not move while its position did would be drawn where it used to be. Readers use
/// <see cref="VersionOf"/>, which answers 0 for a block the store has not numbered yet (a freshly loaded
/// board), rather than an indexer that throws.</para>
///
/// <para>No browser, no dependency injection attributes: the editor registers one per board, with the
/// history depth and block limit taken from its options.</para>
/// </remarks>
public sealed partial class CanvasStore
{
    private readonly List<(IEditorCommand Command, CanvasChangeKind Kind)> _undo = [];
    private readonly List<(IEditorCommand Command, CanvasChangeKind Kind)> _redo = [];
    private readonly Dictionary<Guid, int> _versions = [];
    private LiveMoveSession? _activeMove;
    private LiveResizeSession? _activeResize;

    public CanvasDocument Document { get; private set; } = new();

    public int HistoryDepth { get; init; } = 50;

    public int MaxNodes { get; init; } = 2000;

    public event Action<CanvasChange>? OnChange;

    /// <summary>Counts every change that is an edit. Loading a board is not an edit; autosave compares this.</summary>
    public long ChangeCounter { get; private set; }

    /// <summary>Raised each time a board is loaded, so a component can tell a reload from an edit.</summary>
    public int LoadGeneration { get; private set; }

    public IReadOnlyDictionary<Guid, int> Versions => _versions;

    public int VersionOf(Guid nodeId) => _versions.GetValueOrDefault(nodeId);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Command.Description : null;
    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Command.Description : null;

    /// <summary>True while a drag or resize is under way and has not been committed or cancelled.</summary>
    public bool GestureInProgress => _activeMove is { IsActive: true } || _activeResize is { IsActive: true };

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        CancelLiveSessions();
        var (command, kind) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo();
        _redo.Add((command, kind));
        Notify(kind, IdsOf(command));
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        CancelLiveSessions();
        var (command, kind) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Execute();
        _undo.Add((command, kind));
        Notify(kind, IdsOf(command));
        return true;
    }

    /// <summary>Replaces the board. History is cleared and this does not count as an edit.</summary>
    public void Load(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _activeMove = null;
        _activeResize = null;
        Document = document;
        _undo.Clear();
        _redo.Clear();
        _versions.Clear();
        LoadGeneration++;
        OnChange?.Invoke(new CanvasChange(CanvasChangeKind.Reset, null));
    }

    internal void Execute(IEditorCommand command, CanvasChangeKind kind)
    {
        command.Execute();
        Record(command, kind);
    }

    /// <summary>Pushes a command whose change has already been applied (a live drag's result).</summary>
    internal void Record(IEditorCommand command, CanvasChangeKind kind)
    {
        _undo.Add((command, kind));
        if (_undo.Count > Math.Max(1, HistoryDepth)) _undo.RemoveAt(0);
        _redo.Clear();
        Notify(kind, IdsOf(command));
    }

    internal void Notify(CanvasChangeKind kind, IReadOnlyList<Guid>? nodeIds)
    {
        if (kind != CanvasChangeKind.Reset) ChangeCounter++;

        if (nodeIds is null)
        {
            foreach (var node in Document.Nodes) Bump(node.Id);
        }
        else
        {
            foreach (var id in nodeIds) Bump(id);
        }

        OnChange?.Invoke(new CanvasChange(kind, nodeIds));
    }

    private void Bump(Guid id) => _versions[id] = _versions.GetValueOrDefault(id) + 1;

    private static IReadOnlyList<Guid>? IdsOf(IEditorCommand command) =>
        command is ITouchesNodes touches ? touches.NodeIds.Distinct().ToList() : null;

    private void CancelLiveSessions()
    {
        if (_activeMove is { IsActive: true }) _activeMove.Cancel();
        if (_activeResize is { IsActive: true }) _activeResize.Cancel();
    }

    internal void EndSession(object session)
    {
        if (ReferenceEquals(session, _activeMove)) _activeMove = null;
        if (ReferenceEquals(session, _activeResize)) _activeResize = null;
    }

    // ── Lookups ─────────────────────────────────────────────────────────

    public CanvasNode? FindNode(Guid id) => Document.Nodes.FirstOrDefault(n => n.Id == id);

    public CanvasEdge? FindEdge(Guid id) => Document.Edges.FirstOrDefault(e => e.Id == id);

    public CanvasGroup? FindGroup(Guid id) => Document.Groups.FirstOrDefault(g => g.Id == id);

    public IReadOnlyList<CanvasEdge> EdgesOf(Guid nodeId) =>
        Document.Edges.Where(e => e.FromNodeId == nodeId || e.ToNodeId == nodeId).ToList();

    public IEnumerable<CanvasNode> MembersOf(Guid groupId) => Document.Nodes.Where(n => n.GroupId == groupId);

    /// <summary>The order blocks are drawn in, back to front. DOM order, never an inline z-index.</summary>
    public IReadOnlyList<CanvasNode> NodesInPaintOrder() =>
        Document.Nodes.OrderBy(n => n.Z).ThenBy(n => n.Id).ToList();

    public int AllocateZ() => Document.NextZ++;

    /// <summary>
    /// Stored pictures and files that undo or redo could bring back, so the device sweep keeps them.
    /// </summary>
    public IReadOnlySet<Guid> AssetIdsHeldByHistory() =>
        _undo.Concat(_redo)
            .Select(entry => entry.Command)
            .OfType<IHoldsData>()
            .SelectMany(c => c.HeldData)
            .SelectMany(AssetReferences.AssetIdsOf)
            .ToHashSet();
}
