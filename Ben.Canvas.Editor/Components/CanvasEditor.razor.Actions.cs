using Ben.Canvas.Core.Text;
using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Editor.Services;
using Microsoft.Extensions.Logging;

namespace Ben.Canvas.Editor.Components;

public partial class CanvasEditor
{
    /// <summary>
    /// The single entry for buttons, menu items and keys. An unknown action is logged, never thrown, so a
    /// stale button in a host cannot break the editor.
    /// </summary>
    public async Task RunActionAsync(string action)
    {
        if (!Access.CanEdit && ChangesTheBoard(action))
        {
            Announcer.Say(Access.Reason ?? CanvasCopy.Sentences.ViewOnly);
            return;
        }

        switch (action)
        {
            case "undo":
                if (Store.Undo()) Announcer.Say(Words.UndoOf(Store.RedoDescription));
                break;
            case "redo":
                if (Store.Redo()) Announcer.Say(Words.RedoOf(Store.UndoDescription));
                break;
            case "zoom-in":
                await Bridge.SyncViewportAsync();
                Viewport.ZoomAboutCentre(1.25);
                await Bridge.PushViewportAsync(animate: true);
                break;
            case "zoom-out":
                await Bridge.SyncViewportAsync();
                Viewport.ZoomAboutCentre(0.8);
                await Bridge.PushViewportAsync(animate: true);
                break;
            case "zoom-reset":
                await Bridge.SyncViewportAsync();
                Viewport.ResetZoom();
                await Bridge.PushViewportAsync(animate: true);
                break;
            case "fit":
                FitToContent();
                await Bridge.PushViewportAsync(animate: true);
                break;
            case "toggle-props":
                await Layout.SetPropsOpenAsync(!Layout.PropsOpen);
                break;
            case "help":
                Layout.Close();
                _helpOpen = true;
                break;
            case "add-menu":
                Layout.Open(CanvasLayoutState.SheetAdd);
                break;
            case "more":
                Layout.Open(CanvasLayoutState.SheetMore);
                break;
            case "sheet-close":
                Layout.Close();
                break;
            case "edit":
                if (SingleSelected() is { } editing) await BeginEditAsync(editing.Id);
                break;
            case "duplicate":
                var copies = Store.Duplicate(Selection.NodeIds);
                if (copies.Count > 0) Selection.SelectMany(copies);
                break;
            case "delete":
                DeleteSelection();
                break;
            case "lock":
                ToggleLock();
                break;
            case "front":
                Store.BringToFront(Selection.NodeIds);
                break;
            case "back":
                Store.SendToBack(Selection.NodeIds);
                break;
            case "group":
                if (Store.Group(Selection.NodeIds) is { } group) Selection.SelectGroup(group.Id);
                break;
            case "ungroup":
                Ungroup();
                break;
            case "rename":
                if (Selection.GroupId is { } gid) Selection.BeginRenameGroup(gid);
                break;
            case "connect":
                EnterConnectMode();
                break;
            // ── Presenting ──────────────────────────────────────────────────
            //
            // Ben, 2026-09-16: "presentation mode like miro where you can create the cards like
            // slides." Nothing is made first: SlideOrder reads the running order off the board.
            case "present":
                Layout.Close();
                await Layout.SetPropsOpenAsync(false);
                Selection.Clear();
                if (Presentation.Start(SingleSelected()?.Id)) await ShowCurrentSlideAsync();
                else Announcer.Say(CanvasCopy.Sentences.NothingToPresent);
                break;
            case "present-next":
                if (Presentation.Next()) await ShowCurrentSlideAsync();
                break;
            case "present-previous":
                if (Presentation.Previous()) await ShowCurrentSlideAsync();
                break;
            case "present-stop":
                if (Presentation.Active)
                {
                    Presentation.Stop();
                    FitToContent();
                    await Bridge.PushViewportAsync(animate: true);
                    Announcer.Say(CanvasCopy.Sentences.PresentingStopped);
                }

                break;
            case "case-files":
                Layout.Close();
                _caseFilesOpen = CanPickCaseFiles;
                break;
            case "add-card-here":
                AddBlock(CanvasNodeType.Card, _menuWorld);
                break;
            case "add-text-here":
                AddBlock(CanvasNodeType.Text, _menuWorld);
                break;
            case "save-server":
            case "save-retry":
                await SaveToCaseAsync();
                break;
            case "publish":
                _publishOpen = ServerSession.CanSaveToCase;
                break;
            case "publish-confirmed":
                var publishProblem = await ServerSession.PublishAsync(scene => Snapshots.DrawAsync(_root, scene));
                _publishOpen = false;
                if (publishProblem is null)
                {
                    Toasts.Success(CanvasCopy.Sentences.PublishDone);
                    Announcer.Say(CanvasCopy.Sentences.PublishDone);
                }
                else ShowServerProblem(publishProblem);

                break;
            case "resolve-conflict":
                _conflictOpen = ServerSession.PendingConflict is not null;
                break;
            case "conflict-mine":
                _conflictOpen = false;
                if (await ServerSession.KeepMineAsync() is { } keepProblem) ShowServerProblem(keepProblem);
                else Announcer.Say(CanvasCopy.Sentences.SavedToCase);
                break;
            case "conflict-theirs":
                _conflictOpen = false;
                if (await ServerSession.TakeTheirsAsync() is { } takeProblem) Toasts.Warning(takeProblem);
                else
                {
                    Selection.Clear();
                    Announcer.Say(CanvasCopy.Sentences.TookTheirs);
                }

                break;
            case "conflict-export":
                // The dialog stays open: exporting keeps a copy, the choice still has to be made.
                await RunActionAsync("export");
                break;
            case "paste":
                // The Paste button is handled by the browser's own click (pasteInterop.js), where reading the
                // clipboard is allowed; nothing to do here.
                break;
            case "export":
                var export = await Packages.ExportAsync();
                if (export.Problems.Count > 0) Toasts.Warning(string.Join(" ", export.Problems));
                if (export.DownloadUrl is not null)
                {
                    _downloadUrl = export.DownloadUrl;
                    _downloadName = export.FileName;
                    _downloadOpen = true;
                }

                break;
            case "import":
                await Packages.ChooseFileAsync(_importInput);
                break;
            case "save":
                // Ctrl+S saves to the case when the board can be; otherwise it keeps the device copy current.
                if (ServerSession.CanSaveToCase) await SaveToCaseAsync();
                else await Documents.SaveAsync();
                break;
            default:
                if (action.StartsWith("add-", StringComparison.Ordinal)
                    && Enum.TryParse<CanvasNodeType>(action[4..], ignoreCase: true, out var type))
                {
                    AddBlock(type, null);
                    break;
                }

                LoggerFactory.CreateLogger<CanvasEditor>().LogWarning("Unknown canvas action {Action}.", action);
                break;
        }

        StateHasChanged();
    }

    /// <summary>
    /// The actions a view-only board refuses (R33): everything that changes the board or sends it to the case.
    /// Moving around, selecting, reading, help and export stay allowed.
    /// </summary>
    internal static bool ChangesTheBoard(string action) =>
        action.StartsWith("add-", StringComparison.Ordinal) && action != "add-menu"
        || action is "undo" or "redo" or "edit" or "duplicate" or "delete" or "lock" or "front" or "back" or "group"
            or "ungroup" or "rename" or "connect" or "paste" or "import" or "save-server" or "save-retry" or "publish"
            or "publish-confirmed" or "conflict-mine" or "case-files";

    /// <summary>
    /// Presenting is reading, not writing — so a view-only board presents like any other.
    /// </summary>
    /// <remarks>
    /// Deliberately absent from <see cref="ChangesTheBoard"/>, and worth saying out loud: the meeting
    /// where a case is talked through is exactly the room where somebody without edit access is
    /// driving, and refusing them the walk would make the feature useless where it matters most.
    /// </remarks>
    internal static bool IsPresenting(string action) =>
        action is "present" or "present-next" or "present-previous" or "present-stop";

    private async Task SaveToCaseAsync()
    {
        if (await ServerSession.SaveToCaseAsync() is { } problem) ShowServerProblem(problem);
        else Announcer.Say(CanvasCopy.Sentences.SavedToCase);
    }

    /// <summary>A conflict opens the choice; anything else is a toast that keeps the device copy's reassurance.</summary>
    private void ShowServerProblem(string problem)
    {
        if (ServerSession.PendingConflict is not null)
        {
            _conflictOpen = true;
            return;
        }

        Toasts.Warning(CanvasCopy.Sentences.SaveFailed(problem));
    }

    /// <summary>
    /// Moves the view to the stop being shown, and says where the walk has got to.
    /// </summary>
    /// <remarks>
    /// The rectangle is read from the store at each step rather than from the slide taken when the
    /// walk began, so a card somebody moves while the board is on screen is still framed. A card that
    /// has been deleted has no rectangle at all, and the walk stops rather than fitting the view to
    /// an empty patch of board.
    /// </remarks>
    private async Task ShowCurrentSlideAsync()
    {
        if (Presentation.CurrentRect() is not { } rect)
        {
            Presentation.Stop();
            Toasts.Warning(CanvasCopy.Sentences.SlideGone);
            return;
        }

        Viewport.Fit(rect);
        await Bridge.PushViewportAsync(animate: true);
        if (Presentation.Current is { } slide)
            Announcer.Say(Words.Showing(Presentation.Index + 1, Presentation.Count, slide.Title));
    }

    private CanvasNode? SingleSelected() =>
        Selection.NodeIds.Count == 1 ? Store.FindNode(Selection.NodeIds.First()) : null;

    private void FitToContent()
    {
        var bounds = WorldRect.Bounds(Store.Document.Nodes.Select(CanvasHitTester.RectOf)
            .Concat(Store.Document.Groups.Select(CanvasHitTester.RectOf)));
        if (bounds.IsEmpty) Viewport.Set(CanvasViewport.Identity);
        else Viewport.Fit(bounds);
    }

    /// <summary>Adds a block at a point, or centred in view; repeated adds cascade so they never stack exactly.</summary>
    private void AddBlock(CanvasNodeType type, CanvasPoint? at) =>
        AddBlock(type, at, _ => null);

    /// <summary>
    /// The same, with the chance to fill the block in: <paramref name="fill"/> is handed the default
    /// data and returns what the block should hold, or null to keep the default.
    /// </summary>
    private void AddBlock(CanvasNodeType type, CanvasPoint? at, Func<NodeData, NodeData?> fill)
    {
        if (!Options.Value.EnabledBlocks.Contains(type)) return;
        var d = BlockRegistry.Get(type);

        CanvasPoint centre;
        if (at is { } point)
        {
            centre = point;
            _addCascade = 0;
        }
        else
        {
            var now = DateTime.UtcNow;
            _addCascade = now - _lastAddAt < TimeSpan.FromSeconds(2) ? _addCascade + 1 : 0;
            _lastAddAt = now;
            var mid = Viewport.WorldCentre();
            centre = new CanvasPoint(mid.X + _addCascade * CanvasStoreCascade, mid.Y + _addCascade * CanvasStoreCascade);
        }

        var node = new CanvasNode
        {
            Type = type,
            X = centre.X - d.DefaultWidth / 2,
            Y = centre.Y - d.DefaultHeight / 2,
            Width = d.DefaultWidth,
            Height = d.DefaultHeight,
            Data = d.CreateDefaultData(DateTime.UtcNow),
        };

        if (fill(node.Data) is { } filled) node.Data = filled;

        if (Store.AddNode(node))
        {
            Selection.Select(node.Id);
            Selection.FocusedNodeId = node.Id;
        }
    }

    /// <summary>
    /// Puts a file the case already holds onto the board.
    /// </summary>
    /// <remarks>
    /// The block carries the case's <c>UploadFileId</c> and no <c>AssetId</c>: the bytes are on the
    /// server already, so nothing is copied to this device and the next save has nothing to upload.
    /// That is the whole point of picking rather than dropping — one copy of one piece of evidence.
    /// </remarks>
    private void AddCaseFile(CanvasCaseFile file)
    {
        var kind = CaseFileBlockKind.For(file.FileName, file.ContentType);

        // A host that has turned a block off still gets the file, as a chip. Silently dropping what
        // somebody just chose is the one outcome worth ruling out.
        if (!Options.Value.EnabledBlocks.Contains(kind)) kind = CanvasNodeType.File;
        if (!Options.Value.EnabledBlocks.Contains(kind)) return;

        AddBlock(kind, null, data => CaseFileBlocks.Fill(data, file));

        // The device copy saves itself: CanvasDocumentStore listens to the store's changes.
        Announcer.Say(CanvasCopy.Sentences.AddedFromCase(file.FileName));
    }

    private const double CanvasStoreCascade = Core.Commands.CanvasStore.CascadeOffset;

    private void DeleteSelection()
    {
        if (Selection.NodeIds.Count > 0)
        {
            var count = Selection.NodeIds.Count;
            if (Store.RemoveNodes(Selection.NodeIds.ToList())) Announcer.Say(Words.Deleted(count));
        }
        else if (Selection.EdgeId is { } edgeId)
        {
            Store.Disconnect(edgeId);
        }
        else if (Selection.GroupId is { } groupId)
        {
            Store.Ungroup(groupId);
        }
    }

    private void ToggleLock()
    {
        var nodes = Selection.NodeIds.Select(Store.FindNode).OfType<CanvasNode>().ToList();
        if (nodes.Count == 0) return;
        var lockAll = nodes.Any(n => !n.Locked);
        if (Store.SetLocked(nodes.Select(n => n.Id), lockAll)) Announcer.Say(lockAll ? Words.Locked : Words.Unlocked);
    }

    private void Ungroup()
    {
        if (Selection.GroupId is { } gid)
        {
            Store.Ungroup(gid);
            return;
        }

        var groups = Selection.NodeIds.Select(Store.FindNode).OfType<CanvasNode>().Select(n => n.GroupId).OfType<Guid>().Distinct().ToList();
        foreach (var group in groups) Store.Ungroup(group);
    }
}
