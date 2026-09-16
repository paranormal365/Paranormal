using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Input;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;

namespace Ben.Canvas.Editor.Components;

public partial class CanvasEditor
{
    private enum KeyboardMode { None, Resize, Connect }

    private KeyboardMode _mode;
    private List<Guid> _candidates = [];
    private int _candidateIndex;
    private Guid? _connectFrom;

    private Guid? ConnectCandidate =>
        _mode == KeyboardMode.Connect && _candidates.Count > 0 ? _candidates[_candidateIndex] : null;

    /// <summary>
    /// Everything a key can do. Keyboard-only people can move, resize and connect: R and the arrows resize,
    /// C with the arrows and Enter connects.
    /// </summary>
    public async Task OnEditorKeyDown(string key, bool ctrl, bool shift, bool alt, bool onBoard)
    {
        if (_mode != KeyboardMode.None && !ctrl && !alt)
        {
            if (await HandleModeKeyAsync(key, shift)) { StateHasChanged(); return; }
        }

        var command = CanvasKeyMap.Resolve(key, ctrl, shift, alt, onBoard);
        var step = shift ? 10 : 1;

        // A view-only board (R33): arrows, Enter, F2, R and C would change it without going through RunActionAsync.
        if (!Access.CanEdit && command is CanvasCommand.MoveLeft or CanvasCommand.MoveRight or CanvasCommand.MoveUp or CanvasCommand.MoveDown
                or CanvasCommand.EditSelected or CanvasCommand.Rename or CanvasCommand.ResizeMode or CanvasCommand.ConnectMode
                or CanvasCommand.GrowLeft or CanvasCommand.GrowRight or CanvasCommand.GrowUp or CanvasCommand.GrowDown)
        {
            Announcer.Say(Access.Reason ?? Ben.Canvas.Core.Text.CanvasCopy.Sentences.ViewOnly);
            return;
        }

        switch (command)
        {
            case CanvasCommand.None:
                return;
            case CanvasCommand.DeleteSelection:
                await RunActionAsync("delete");
                break;
            case CanvasCommand.MoveLeft:
                Nudge(-step, 0);
                break;
            case CanvasCommand.MoveRight:
                Nudge(step, 0);
                break;
            case CanvasCommand.MoveUp:
                Nudge(0, -step);
                break;
            case CanvasCommand.MoveDown:
                Nudge(0, step);
                break;
            case CanvasCommand.GrowLeft:
                GrowFromSelection(CanvasSide.Left);
                break;
            case CanvasCommand.GrowRight:
                GrowFromSelection(CanvasSide.Right);
                break;
            case CanvasCommand.GrowUp:
                GrowFromSelection(CanvasSide.Top);
                break;
            case CanvasCommand.GrowDown:
                GrowFromSelection(CanvasSide.Bottom);
                break;
            case CanvasCommand.ClearSelection:
                await Bridge.CancelAsync();
                _menu?.Close();
                Selection.Clear();
                break;
            case CanvasCommand.SelectAll:
                Selection.SelectMany(Store.Document.Nodes.Select(n => n.Id));
                break;
            case CanvasCommand.Duplicate:
                await RunActionAsync("duplicate");
                break;
            case CanvasCommand.Copy:
            case CanvasCommand.Cut:
                // Clipboard arrives with the paste service (M4).
                break;
            case CanvasCommand.Undo:
                await RunActionAsync("undo");
                break;
            case CanvasCommand.Redo:
                await RunActionAsync("redo");
                break;
            case CanvasCommand.ZoomIn:
                await RunActionAsync("zoom-in");
                break;
            case CanvasCommand.ZoomOut:
                await RunActionAsync("zoom-out");
                break;
            case CanvasCommand.ZoomReset:
                await RunActionAsync("zoom-reset");
                break;
            case CanvasCommand.FitToContent:
                await RunActionAsync("fit");
                break;
            case CanvasCommand.Group:
                await RunActionAsync("group");
                break;
            case CanvasCommand.Ungroup:
                await RunActionAsync("ungroup");
                break;
            case CanvasCommand.ToggleLock:
                await RunActionAsync("lock");
                break;
            case CanvasCommand.BringToFront:
                await RunActionAsync("front");
                break;
            case CanvasCommand.SendToBack:
                await RunActionAsync("back");
                break;
            case CanvasCommand.Save:
                // Claimed so the browser's save dialog never opens. Saves to this device now; to the case in M6.
                await RunActionAsync("save");
                break;
            case CanvasCommand.ToggleHelp:
                _helpOpen = true;
                break;
            case CanvasCommand.EditSelected:
                if (Selection.FocusedNodeId is { } focused && Store.FindNode(focused) is not null) await BeginEditAsync(focused);
                break;
            case CanvasCommand.Rename:
                if (Selection.GroupId is { } group) Selection.BeginRenameGroup(group);
                else if (Selection.FocusedNodeId is { } renaming) await BeginEditAsync(renaming);
                break;
            case CanvasCommand.ResizeMode:
                EnterResizeMode();
                break;
            case CanvasCommand.ConnectMode:
                EnterConnectMode();
                break;
            case CanvasCommand.AddText:
                await RunActionAsync("add-text");
                break;
            case CanvasCommand.AddCard:
                await RunActionAsync("add-card");
                break;
            case CanvasCommand.AddMessage:
                await RunActionAsync("add-message");
                break;
            case CanvasCommand.AddImage:
                await RunActionAsync("add-image");
                break;
            case CanvasCommand.AddLink:
                await RunActionAsync("add-link");
                break;
            case CanvasCommand.NextConnector:
                CycleConnector(1);
                break;
            case CanvasCommand.PreviousConnector:
                CycleConnector(-1);
                break;
        }

        StateHasChanged();
    }

    /// <summary>
    /// Ctrl+Shift+Arrow: the next block on that side of the selected one, already joined to it.
    /// </summary>
    /// <remarks>
    /// The keyboard half of the side handles. Nothing selected, or several, and there is no one block
    /// to grow from — said out loud rather than silently ignored, because a shortcut that does nothing
    /// reads as broken.
    /// </remarks>
    private void GrowFromSelection(CanvasSide side)
    {
        if (SingleSelected() is not { } node)
        {
            Announcer.Say(Words.GrowNeedsOneBlock);
            return;
        }

        Bridge.GrowFrom(node.Id, side);
    }

    private void Nudge(double dx, double dy)
    {
        var ids = Selection.NodeIds.ToList();
        if (ids.Count == 0) return;
        if (Store.MoveNodes(ids, dx, dy) && Store.FindNode(ids[0]) is { } first)
            Announcer.Say(Words.MovedTo(first.X, first.Y));
        else if (ids.Select(Store.FindNode).All(n => n?.Locked == true))
            Announcer.Say(Words.LockedCannotMove);
    }

    private void EnterResizeMode()
    {
        if (SingleSelected() is not { Locked: false } node) return;
        var d = BlockRegistry.Get(node.Type);
        if (!d.ResizableWidth && !d.ResizableHeight) return;
        _mode = KeyboardMode.Resize;
        Announcer.Say(Words.ResizeModeOn);
    }

    private void EnterConnectMode()
    {
        if (SingleSelected() is not { } from) return;
        var centre = CanvasHitTesterCentre(from);
        _candidates = Store.Document.Nodes
            .Where(n => n.Id != from.Id)
            .OrderBy(n => Distance(CanvasHitTesterCentre(n), centre))
            .Select(n => n.Id)
            .ToList();
        if (_candidates.Count == 0) return;
        _connectFrom = from.Id;
        _candidateIndex = 0;
        _mode = KeyboardMode.Connect;
        Selection.SetConnectMode(true);
        AnnounceCandidate();
    }

    private async Task<bool> HandleModeKeyAsync(string key, bool shift)
    {
        var step = shift ? 10 : 1;

        if (_mode == KeyboardMode.Resize)
        {
            if (SingleSelected() is not { } node) { _mode = KeyboardMode.None; return false; }
            switch (key)
            {
                case "ArrowRight": ResizeBy(node, step, 0); return true;
                case "ArrowLeft": ResizeBy(node, -step, 0); return true;
                case "ArrowDown": ResizeBy(node, 0, step); return true;
                case "ArrowUp": ResizeBy(node, 0, -step); return true;
                case "r" or "R" or "Escape" or "Enter":
                    _mode = KeyboardMode.None;
                    Announcer.Say(Words.ResizeModeOff);
                    return true;
            }

            return false;
        }

        if (_mode == KeyboardMode.Connect)
        {
            switch (key)
            {
                case "ArrowRight" or "ArrowDown":
                    _candidateIndex = (_candidateIndex + 1) % _candidates.Count;
                    AnnounceCandidate();
                    return true;
                case "ArrowLeft" or "ArrowUp":
                    _candidateIndex = (_candidateIndex - 1 + _candidates.Count) % _candidates.Count;
                    AnnounceCandidate();
                    return true;
                case "Enter":
                    if (_connectFrom is { } from && ConnectCandidate is { } to && Store.Connect(from, to) is { } edge)
                    {
                        Announcer.Say(Words.Connected(TitleOf(from), TitleOf(to)));
                        LeaveConnectMode();
                        Selection.SelectEdge(edge.Id);
                    }
                    else
                    {
                        LeaveConnectMode();
                    }

                    return true;
                case "Escape" or "c" or "C":
                    LeaveConnectMode();
                    Announcer.Say(Words.ConnectCancelled);
                    await Task.CompletedTask;
                    return true;
            }
        }

        return false;
    }

    private void LeaveConnectMode()
    {
        _mode = KeyboardMode.None;
        _candidates = [];
        _connectFrom = null;
        Selection.SetConnectMode(false);
    }

    private void ResizeBy(CanvasNode node, double dw, double dh)
    {
        if (Store.ResizeNode(node.Id, node.X, node.Y, node.Width + dw, node.Height + dh))
            Announcer.Say(Words.ResizedTo(node.Width, node.Height));
    }

    private void AnnounceCandidate()
    {
        if (ConnectCandidate is { } id) Announcer.Say(Words.ConnectTo(TitleOf(id)));
    }

    private void CycleConnector(int direction)
    {
        var anchor = Selection.FocusedNodeId ?? Selection.NodeIds.FirstOrDefault();
        var edges = anchor == Guid.Empty ? Store.Document.Edges.ToList() : Store.EdgesOf(anchor).ToList();
        if (edges.Count == 0) return;
        var current = Selection.EdgeId is { } selected ? edges.FindIndex(e => e.Id == selected) : -1;
        var next = edges[((current + direction) % edges.Count + edges.Count) % edges.Count];
        Selection.SelectEdge(next.Id);
        Announcer.Say($"Connector from {TitleOf(next.FromNodeId)} to {TitleOf(next.ToNodeId)}.");
        if (anchor != Guid.Empty) Selection.FocusedNodeId = anchor;
    }

    private string TitleOf(Guid nodeId) => Store.FindNode(nodeId) is { } n ? NodeWords.Title(n) : "a block";

    private static (double X, double Y) CanvasHitTesterCentre(CanvasNode n) => (n.X + n.Width / 2, n.Y + n.Height / 2);

    private static double Distance((double X, double Y) a, (double X, double Y) b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
