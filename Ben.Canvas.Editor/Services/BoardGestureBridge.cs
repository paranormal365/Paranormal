using System.Globalization;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

// Payloads match boardGestures.js exactly. Sides travel as strings ("Top", "Right"...) because the
// interop serializer has no enum converter.

public sealed record MoveBegin(Guid[] NodeIds, Guid[] GroupIds);

public sealed record EdgeInfo(Guid Id, Guid FromNodeId, Guid ToNodeId, string? FromSide, string? ToSide);

public sealed record MoveContext(double[] GuidesX, double[] GuidesY, double Threshold, bool SnapEnabled, Guid[] NodeIds, EdgeInfo[] Edges);

public sealed record ResizeBegin(Guid? NodeId, Guid? GroupId, string Handle);

public sealed record ResizeContext(double X, double Y, double Width, double Height, double MinWidth, double MinHeight, EdgeInfo[] Edges);

public sealed record MoveEnd(Guid[] NodeIds, Guid[] GroupIds, double Dx, double Dy);

public sealed record ResizeEnd(Guid? NodeId, Guid? GroupId, string Handle, double Dx, double Dy, bool KeepAspect);

public sealed record MarqueeEnd(double X, double Y, double Width, double Height, bool Additive);

public sealed record TapInfo(Guid? NodeId, Guid? EdgeId, Guid? GroupId, double WorldX, double WorldY, bool Shift, bool Ctrl, string PointerType, bool LockedDrag, string? Port = null);

public sealed record ConnectEnd(Guid FromNodeId, string FromSide, Guid? ToNodeId, string? ToPort, double WorldX, double WorldY);

public sealed record ViewportChanged(double PanX, double PanY, double Zoom);

public sealed record BoardResized(double Width, double Height);

public sealed record PointerMenu(Guid? NodeId, Guid? EdgeId, Guid? GroupId, double ClientX, double ClientY, double WorldX, double WorldY);

/// <summary>
/// The C# half of the board's gesture contract.
/// </summary>
/// <remarks>
/// <para>The script previews a drag with CSS and calls here once it ends. Begin hands it the snap guides and
/// the connectors to redraw; End re-snaps in C# and records one undo entry. Nothing the script previewed
/// is trusted: a rounding error in the browser cannot land a block anywhere C# would not have put it.</para>
///
/// <para>Any exception inside a callback cancels the session and clears <see cref="CanvasViewportState.GestureActive"/>.
/// A stuck flag would stop the board rendering for good.</para>
/// </remarks>
public sealed class BoardGestureBridge : IAsyncDisposable
{
    public const string ModulePath = "js/boardGestures.js";

    private readonly CanvasStore _store;
    private readonly SelectionState _selection;
    private readonly CanvasViewportState _viewport;
    private readonly AnnouncerService _announcer;
    private readonly CanvasEditorOptions _options;
    private readonly IJSRuntime _js;
    private readonly ILogger _log;
    private readonly BoardAccess _access;

    private IJSObjectReference? _module;
    private DotNetObjectReference<BoardGestureBridge>? _self;
    private ElementReference? _board;
    private LiveMoveSession? _move;
    private LiveResizeSession? _resize;

    public BoardGestureBridge(CanvasStore store, SelectionState selection, CanvasViewportState viewport, AnnouncerService announcer,
        IOptions<CanvasEditorOptions> options, IJSRuntime js, ILogger<BoardGestureBridge>? log = null, BoardAccess? access = null)
    {
        _store = store;
        _selection = selection;
        _viewport = viewport;
        _announcer = announcer;
        _options = options.Value;
        _js = js;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _access = access ?? new BoardAccess();
        _access.Changed += () => _ = PushReadOnlyAsync();
    }

    /// <summary>A block asked to be edited (double-click, Enter, the menu).</summary>
    public event Action<Guid>? EditRequested;

    /// <summary>A group label asked to be renamed.</summary>
    public event Action<Guid>? RenameGroupRequested;

    /// <summary>Right-click or long-press: open the context menu here.</summary>
    public event Action<PointerMenu>? ContextMenuRequested;

    public async Task AttachAsync(ElementReference board)
    {
        _board = board;
        _module ??= await CanvasModules.ImportAsync(_js, "js/boardGestures.js");
        _self ??= DotNetObjectReference.Create(this);
        var vp = _viewport.Current;
        await _module.InvokeVoidAsync("attach", board, _self, new
        {
            dragThresholdMouse = 4,
            dragThresholdTouch = 10,
            longPressMs = 500,
            longPressTolerance = 10,
            doubleTapMs = 300,
            doubleTapPx = 24,
            minZoom = CanvasViewport.MinZoom,
            maxZoom = CanvasViewport.MaxZoom,
            initial = new { panX = vp.PanX, panY = vp.PanY, zoom = vp.Zoom },
            readOnly = !_access.CanEdit,
        });
    }

    /// <summary>Tells the script the board became view-only or editable, so presses on blocks pan instead of moving them (R33).</summary>
    private async Task PushReadOnlyAsync()
    {
        if (_module is null || _board is null) return;
        try { await _module.InvokeVoidAsync("setReadOnly", _board.Value, !_access.CanEdit); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException) { }
    }

    /// <summary>A view-only board refuses a change that reached C# anyway (C# is the authority; the script only helps).</summary>
    private bool RefuseViewOnly()
    {
        if (_access.CanEdit) return false;
        _viewport.SetGestureActive(false);
        _announcer.Say(_access.Reason ?? Core.Text.CanvasCopy.Sentences.ViewOnly);
        return true;
    }

    /// <summary>Sends C#'s camera to the board, after a button, key or fit changed it.</summary>
    public async Task PushViewportAsync(bool animate = false)
    {
        if (_module is null || _board is null) return;
        var vp = _viewport.Current;
        await _module.InvokeVoidAsync("setViewport", _board.Value, vp.PanX, vp.PanY, vp.Zoom, animate);
    }

    /// <summary>Reads the camera from the board, so a C#-driven zoom never starts from a stale value.</summary>
    public async Task SyncViewportAsync()
    {
        if (_module is null || _board is null) return;
        var vp = await _module.InvokeAsync<ViewportChanged>("getViewport", _board.Value);
        _viewport.Set(new CanvasViewport(vp.PanX, vp.PanY, vp.Zoom));
    }

    public async Task CancelAsync()
    {
        if (_module is null || _board is null) return;
        await _module.InvokeVoidAsync("cancel", _board.Value);
    }

    public async Task DetachAsync()
    {
        if (_module is null || _board is null) return;
        try { await _module.InvokeVoidAsync("detach", _board.Value); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    // ── Drag ────────────────────────────────────────────────────────────

    [JSInvokable]
    public Task<MoveContext> BeginMove(MoveBegin begin) => Guard(() =>
    {
        if (RefuseViewOnly()) return Task.FromResult(new MoveContext([], [], 0, false, [], []));
        var zoom = _viewport.Current.Zoom;
        var moving = new HashSet<Guid>(begin.NodeIds ?? []);
        foreach (var gid in begin.GroupIds ?? []) foreach (var member in _store.MembersOf(gid)) moving.Add(member.Id);

        var near = _viewport.VisibleWorldRect();
        var guides = _options.SnapEnabled
            ? SnapGuideCollector.Collect(_store.Document.Nodes, moving, near.Inflate(Math.Max(near.Width, near.Height)))
            : SnapGuides.Empty;
        var threshold = ViewportMath.ScreenPxToWorld(_viewport.Current, _options.SnapThresholdPx);

        _move = _store.BeginMove(begin.NodeIds ?? [], begin.GroupIds ?? [], guides, threshold, _options.SnapEnabled, _options.SnapToGrid ? _options.GridSize : 0);
        _viewport.SetGestureActive(true);

        var edges = _move.NodeIds.SelectMany(_store.EdgesOf).DistinctBy(e => e.Id).Select(Info).ToArray();
        return Task.FromResult(new MoveContext(guides.X.ToArray(), guides.Y.ToArray(), threshold, _options.SnapEnabled, _move.NodeIds.ToArray(), edges));
    }, () => new MoveContext([], [], 0, false, [], []));

    [JSInvokable]
    public Task OnMoveEnd(MoveEnd end) => Guard(() =>
    {
        var session = _move;
        _move = null;
        if (session is null) return Task.CompletedTask;

        session.Move(end.Dx, end.Dy);
        var moved = session.Commit();
        _viewport.SetGestureActive(false);

        if (moved && session.NodeIds.Count > 0 && _store.FindNode(session.NodeIds[0]) is { } primary)
            _announcer.Say(Words.MovedTo(primary.X, primary.Y));

        return Task.CompletedTask;
    });

    // ── Resize ──────────────────────────────────────────────────────────

    [JSInvokable]
    public Task<ResizeContext?> BeginResize(ResizeBegin begin) => Guard<ResizeContext?>(() =>
    {
        if (RefuseViewOnly()) return Task.FromResult<ResizeContext?>(null);
        _resize = begin.GroupId is { } gid ? _store.BeginResizeGroup(gid, begin.Handle) : begin.NodeId is { } nid ? _store.BeginResize(nid, begin.Handle) : null;
        if (_resize is null) return Task.FromResult<ResizeContext?>(null);

        _viewport.SetGestureActive(true);
        var rect = _resize.Original;
        double minW, minH;
        EdgeInfo[] edges = [];
        if (begin.NodeId is { } id && _store.FindNode(id) is { } node)
        {
            var d = BlockRegistry.Get(node.Type);
            (minW, minH) = (d.MinWidth, d.MinHeight);
            edges = _store.EdgesOf(id).Select(Info).ToArray();
        }
        else
        {
            (minW, minH) = (BlockRegistry.GroupMinWidth, BlockRegistry.GroupMinHeight);
        }

        return Task.FromResult<ResizeContext?>(new ResizeContext(rect.X, rect.Y, rect.Width, rect.Height, minW, minH, edges));
    }, () => null);

    [JSInvokable]
    public Task OnResizeEnd(ResizeEnd end) => Guard(() =>
    {
        var session = _resize;
        _resize = null;
        if (session is null) return Task.CompletedTask;

        var rect = session.Move(end.Dx, end.Dy, end.KeepAspect);
        var changed = session.Commit();
        _viewport.SetGestureActive(false);
        if (changed) _announcer.Say(Words.ResizedTo(rect.Width, rect.Height));
        return Task.CompletedTask;
    });

    // ── Selection ───────────────────────────────────────────────────────

    [JSInvokable]
    public Task OnMarquee(MarqueeEnd end) => Guard(() =>
    {
        var ids = CanvasHitTester.NodesIn(_store.Document.Nodes, new WorldRect(end.X, end.Y, end.Width, end.Height));
        if (ids.Count == 0 && !end.Additive) _selection.Clear();
        else _selection.SelectMany(ids, end.Additive);
        return Task.CompletedTask;
    });

    [JSInvokable]
    public Task OnTap(TapInfo tap) => Guard(() =>
    {
        // A side handle pressed and released without a drag asks for the next block on that side
        // (Ben, 2026-09-16: "Can we make it like Miro?"). Dragging the same handle still aims.
        if (tap.NodeId is { } grower && ParseSide(tap.Port) is { } growSide && _store.FindNode(grower) is not null)
        {
            GrowFrom(grower, growSide);
            return Task.CompletedTask;
        }

        if (tap.NodeId is { } nodeId && _store.FindNode(nodeId) is not null)
        {
            if (_selection.EditingNodeId == nodeId) return Task.CompletedTask;
            if (tap.Shift || tap.Ctrl) _selection.Toggle(nodeId);
            else _selection.Select(nodeId);
            if (tap.LockedDrag) _announcer.Say(Words.LockedCannotMove);
            return Task.CompletedTask;
        }

        if (tap.GroupId is { } groupId && _store.FindGroup(groupId) is not null)
        {
            _selection.SelectGroup(groupId);
            return Task.CompletedTask;
        }

        var edgeId = tap.EdgeId is { } direct && _store.FindEdge(direct) is not null ? direct : EdgeNear(tap.WorldX, tap.WorldY);
        if (edgeId is { } edge) _selection.SelectEdge(edge);
        else _selection.Clear();
        return Task.CompletedTask;
    });

    [JSInvokable]
    public Task OnDoubleTap(TapInfo tap) => Guard(() =>
    {
        if (tap.NodeId is { } nodeId && _store.FindNode(nodeId) is not null)
        {
            EditRequested?.Invoke(nodeId);
            return Task.CompletedTask;
        }

        if (RefuseViewOnly()) return Task.CompletedTask;

        if (tap.GroupId is { } groupId && _store.FindGroup(groupId) is not null)
        {
            RenameGroupRequested?.Invoke(groupId);
            return Task.CompletedTask;
        }

        var descriptor = BlockRegistry.Get(CanvasNodeType.Text);
        if (!_options.EnabledBlocks.Contains(CanvasNodeType.Text)) return Task.CompletedTask;
        var node = new CanvasNode
        {
            Type = CanvasNodeType.Text,
            X = tap.WorldX - descriptor.DefaultWidth / 2,
            Y = tap.WorldY - descriptor.DefaultHeight / 2,
            Width = descriptor.DefaultWidth,
            Height = descriptor.DefaultHeight,
            Data = descriptor.CreateDefaultData(DateTime.UtcNow),
        };

        if (_store.AddNode(node))
        {
            _selection.Select(node.Id);
            EditRequested?.Invoke(node.Id);
        }

        return Task.CompletedTask;
    });

    [JSInvokable]
    public Task OnConnectEnd(ConnectEnd end) => Guard(() =>
    {
        _viewport.SetGestureActive(false);
        if (RefuseViewOnly()) return Task.CompletedTask;

        // Let go over nothing and the connector gets something to land on, made where it was dropped.
        // A drag that ends back inside the block it started from is not a place to put anything, so
        // that one is placed beside instead.
        if (end.ToNodeId is null && ParseSide(end.FromSide) is { } dropSide && _store.FindNode(end.FromNodeId) is { } source)
        {
            var inside = CanvasHitTester.RectOf(source).Contains(end.WorldX, end.WorldY);
            GrowFrom(end.FromNodeId, dropSide, inside ? null : new CanvasPoint(end.WorldX, end.WorldY));
            return Task.CompletedTask;
        }

        if (end.ToNodeId is not { } to || to == end.FromNodeId) return Task.CompletedTask;

        var edge = _store.Connect(end.FromNodeId, to, ParseSide(end.FromSide), ParseSide(end.ToPort));
        if (edge is not null)
        {
            _selection.SelectEdge(edge.Id);
            _announcer.Say(Words.Connected(Title(end.FromNodeId), Title(to)));
        }

        return Task.CompletedTask;
    });

    [JSInvokable]
    public Task OnGestureCancelled() => Guard(() =>
    {
        _move?.Cancel();
        _resize?.Cancel();
        _move = null;
        _resize = null;
        _viewport.SetGestureActive(false);
        return Task.CompletedTask;
    });

    // ── Camera and menus ────────────────────────────────────────────────

    [JSInvokable]
    public Task OnViewportChanged(ViewportChanged vp)
    {
        _viewport.Set(new CanvasViewport(vp.PanX, vp.PanY, vp.Zoom));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnBoardResized(BoardResized size)
    {
        _viewport.SetBoardSize(size.Width, size.Height);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnLongPress(PointerMenu menu) => OnContextMenu(menu);

    [JSInvokable]
    public Task OnContextMenu(PointerMenu menu) => Guard(() =>
    {
        if (menu.NodeId is { } nodeId && !_selection.IsSelected(nodeId)) _selection.Select(nodeId);
        else if (menu.EdgeId is { } edgeId) _selection.SelectEdge(edgeId);
        else if (menu.GroupId is { } groupId) _selection.SelectGroup(groupId);
        ContextMenuRequested?.Invoke(menu);
        return Task.CompletedTask;
    });

    // ── Helpers ─────────────────────────────────────────────────────────

    private Guid? EdgeNear(double worldX, double worldY)
    {
        var paths = _store.Document.Edges
            .Select(e => (e, from: _store.FindNode(e.FromNodeId), to: _store.FindNode(e.ToNodeId)))
            .Where(t => t.from is not null && t.to is not null)
            .Select(t => (t.e.Id, EdgeGeometry.Resolve(CanvasHitTester.RectOf(t.from!), CanvasHitTester.RectOf(t.to!), t.e.FromSide, t.e.ToSide)))
            .ToList();
        return BezierHitTester.HitTest(paths, new CanvasPoint(worldX, worldY), ViewportMath.ScreenPxToWorld(_viewport.Current, 8));
    }

    private string Title(Guid nodeId) => _store.FindNode(nodeId) is { } n ? NodeWords.Title(n) : "a block";

    private static EdgeInfo Info(CanvasEdge e) => new(e.Id, e.FromNodeId, e.ToNodeId, e.FromSide?.ToString(), e.ToSide?.ToString());

    /// <summary>
    /// The kinds a side handle can make, in the order it falls back through.
    /// </summary>
    /// <remarks>
    /// Only the blocks that mean something empty. A handle that made an empty picture, recording or
    /// file would make a box with nothing in it and no way to fill it from here; a map at least has
    /// somewhere to search, but a train of thought is written in notes and cards, so those come first.
    /// </remarks>
    private static readonly CanvasNodeType[] GrowKinds =
        [CanvasNodeType.Card, CanvasNodeType.Text, CanvasNodeType.Message];

    /// <summary>
    /// Makes the next block out of an existing one's side and joins them, then opens it for typing.
    /// </summary>
    /// <param name="at">Where it was dropped, or null to have it placed beside.</param>
    /// <remarks>
    /// Ben, 2026-09-16: "Can we make it like Miro?" The new block is the same kind as the one it came
    /// from — a card grows cards, a note grows notes — because on a board of one kind of thing, being
    /// handed a different kind is a correction to make rather than a thought to write down.
    /// </remarks>
    public void GrowFrom(Guid fromId, CanvasSide side, CanvasPoint? at = null)
    {
        if (RefuseViewOnly()) return;
        if (_store.FindNode(fromId) is not { } from) return;

        var type = GrowKinds.Contains(from.Type) && _options.EnabledBlocks.Contains(from.Type)
            ? from.Type
            : GrowKinds.FirstOrDefault(_options.EnabledBlocks.Contains, CanvasNodeType.Text);
        if (!_options.EnabledBlocks.Contains(type)) return;

        // The same size as the block it grew from, when it is the same kind: a board of cards
        // somebody has made taller stays a board of blocks that match.
        var descriptor = BlockRegistry.Get(type);
        var width = type == from.Type && descriptor.ResizableWidth ? from.Width : descriptor.DefaultWidth;
        var height = type == from.Type && descriptor.ResizableHeight ? from.Height : descriptor.DefaultHeight;

        var node = new CanvasNode
        {
            Type = type,
            Width = width,
            Height = height,
            ColorKey = type == from.Type ? from.ColorKey : null,
            Data = descriptor.CreateDefaultData(DateTime.UtcNow),
        };

        if (_store.AddConnected(fromId, side, node, at) is null) return;

        _selection.Select(node.Id);
        _selection.FocusedNodeId = node.Id;
        _announcer.Say(Words.GrewFrom(descriptor.DisplayName, side, Title(fromId)));

        // Straight into typing, as on the boards this copies: the gesture was "and then this", and
        // stopping to click the new block before writing in it loses the thought.
        EditRequested?.Invoke(node.Id);
    }

    internal static CanvasSide? ParseSide(string? side) =>
        Enum.TryParse<CanvasSide>(side, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

    private Task Guard(Func<Task> body) => Guard<object?>(async () => { await body(); return null; }, () => null);

    private async Task<T> Guard<T>(Func<Task<T>> body, Func<T> fallback)
    {
        try
        {
            return await body();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A board gesture failed; the gesture was cancelled.");
            _move?.Cancel();
            _resize?.Cancel();
            _move = null;
            _resize = null;
            _viewport.SetGestureActive(false);
            return fallback();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DetachAsync();
        _self?.Dispose();
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }
}

/// <summary>Sentences the board announces, with numbers rounded and written the same in every culture.</summary>
public static class Words
{
    private static string N(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    public static string MovedTo(double x, double y) => $"Moved to {N(x)}, {N(y)}.";
    public static string ResizedTo(double w, double h) => $"Resized to {N(w)} by {N(h)}.";
    public static string Connected(string a, string b) => $"Connected {a} to {b}.";

    public const string GrowNeedsOneBlock = "Select one block first, then Ctrl+Shift and an arrow key to add the next one beside it.";

    public static string GrewFrom(string kind, CanvasSide side, string from) =>
        $"Added {kind.ToLowerInvariant()} to the {side.ToString().ToLowerInvariant()} of {from}, connected.";
    public static string Deleted(int n) => n == 1 ? "Deleted 1 item." : $"Deleted {n} items.";
    public static string UndoOf(string? description) => $"Undo: {description ?? "nothing"}.";
    public static string RedoOf(string? description) => $"Redo: {description ?? "nothing"}.";
    public const string Locked = "Locked.";
    public const string Unlocked = "Unlocked.";
    public const string LockedCannotMove = "That item is locked. Unlock it to move it.";
    public const string ResizeModeOn = "Resize mode. Use the arrow keys, then press Enter.";
    public const string ResizeModeOff = "Resize mode off.";
    public static string ConnectTo(string title) => $"Connect to {title}? Press Enter.";
    public const string ConnectCancelled = "Connect cancelled.";
    public const string BoardHelp =
        "Use Tab to move between items and arrow keys to move the selected item. Press R, then arrows, to resize. Press C, then arrows and Enter, to connect. Enter edits, Delete removes. Press ? for all shortcuts.";
}

/// <summary>Short names for blocks, for labels and announcements.</summary>
public static class NodeWords
{
    public static string Title(CanvasNode node)
    {
        var text = node.Data switch
        {
            CardData c => string.IsNullOrWhiteSpace(c.Title) ? null : c.Title,
            MessageData m => string.IsNullOrWhiteSpace(m.Author) ? Core.Paste.PasteHtmlAllowList.VisibleText(m.Html) : m.Author,
            MapData m => m.Address ?? (m.Latitude != 0 || m.Longitude != 0
                ? string.Create(CultureInfo.InvariantCulture, $"{m.Latitude:F5}, {m.Longitude:F5}")
                : null),
            ImageData i => i.Caption,
            LinkData l => l.Title ?? HostOf(l.Url),
            TextData t => t.Text,
            FileData f => f.FileName,
            _ => null,
        };

        var clean = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length == 0) return BlockRegistry.Get(node.Type).DisplayName;
        return clean.Length <= 40 ? clean : clean[..39] + "…";
    }

    public static string Aria(CanvasNode node, bool selected, string? groupLabel)
    {
        var d = BlockRegistry.Get(node.Type);
        var label = $"{d.DisplayName}: {Title(node)}";
        if (selected) label += ", selected";
        if (node.Locked) label += ", locked";
        if (!string.IsNullOrWhiteSpace(groupLabel)) label += $", in group {groupLabel}";
        return label;
    }

    public static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri.Host : null;
}
