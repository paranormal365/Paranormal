using Ben.Canvas.Core.Text;
using Microsoft.Extensions.Logging;

namespace Ben.Canvas.Editor.Components;

public partial class CanvasEditor
{
    /// <summary>
    /// Startup, in an order later milestones insert into by number.
    /// </summary>
    /// <remarks>
    /// Each step is wrapped on its own: a keyboard module that fails to import must not leave the board
    /// without gestures.
    /// 1 keyboard - 2 gestures - 2b paste - 3 asset store - 4 document store - 5 restore (or open from the
    /// server, M6), then push the view - 6 autosave - 7 unload guard - 8 sweep unused assets.
    /// Autosave starts after the restore, so reading the last board is never itself an edit.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var log = LoggerFactory.CreateLogger<CanvasEditor>();

        // 1
        await Step(log, "keyboard", () => Keyboard.RegisterAsync(this));

        // 1b: the layout, so the phone and iPad choices are known before anything opens.
        await Step(log, "layout", Layout.StartAsync);

        // 2
        if (_board is not null) await Step(log, "gestures", () => Bridge.AttachAsync(_board.BoardElement));

        // 2b
        if (_board is not null) await Step(log, "paste", () => Paste.AttachAsync(_board.BoardElement, _catcher));

        // 3
        await Step(log, "assets", Assets.StartAsync);

        // 4
        await Step(log, "documents", Documents.StartAsync);

        // 5
        await Step(log, "restore", RestoreAsync);

        // 6
        Documents.EnableAutosave();

        // 7
        await Step(log, "unload guard", UnloadGuard.StartAsync);

        _ready = true;
        StateHasChanged();

        // 7b: link cards on the board that never got a preview, or only a host a day ago.
        _ = ResolveLinksAsync(includeStale: true);

        // 8: after startup and never awaited, so a slow file listing cannot hold up the board.
        _ = Step(log, "sweep", () => Documents.SweepUnusedAssetsAsync());
    }

    /// <summary>Reopens the last board and its view, or starts a new board.</summary>
    private async Task RestoreAsync()
    {
        var (restored, problem) = await Documents.RestoreLastActiveAsync();
        if (!restored) Documents.New(CaseId);
        if (problem is not null) Toasts.Warning(CanvasCopy.Sentences.RestoreFailed(problem));

        // The server comes after the device copy, so a slow or failed request still leaves a board open (M6).
        var opened = await OpenFromServerAsync();

        if ((restored || opened) && await Documents.LoadViewAsync() is { } view) Viewport.Set(view);
        else if (opened) FitToContent();
        await Bridge.PushViewportAsync();
    }

    /// <summary>What was last opened from the server, so a re-render or a second sign-in event does not open it again.</summary>
    private (Guid?, Guid?)? _serverOpened;

    /// <summary>
    /// The host usually learns the case (from the site's hand-off) after the editor has started, so a case or board
    /// that arrives later is opened then.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_ready && (OpenServerDocumentId is not null || CaseId is not null)) OnSignInChanged();
    }

    /// <summary>Opens the board the host asked for, or the case's board. True when a server board was opened.</summary>
    private async Task<bool> OpenFromServerAsync()
    {
        var target = (OpenServerDocumentId, CaseId);
        if (target is (null, null) || _serverOpened == target || !ServerSession.IsServerAvailable) return false;
        _serverOpened = target;
        var before = Documents.CurrentServerId;

        var problem = OpenServerDocumentId is { } id
            ? await ServerSession.OpenAsync(id)
            : await ServerSession.OpenForCaseAsync(CaseId!.Value, OrganizationId);

        Documents.SetOrganization(OrganizationId);
        if (ServerSession.PendingConflict is not null) _conflictOpen = true;
        else if (problem is not null) Toasts.Warning(problem);

        return Documents.CurrentServerId is not null && Documents.CurrentServerId != before;
    }

    /// <summary>Signing in after the editor started (another page, or the chip) opens the case's board then.</summary>
    private void OnSignInChanged() => _ = InvokeAsync(async () =>
    {
        if (!_ready || SignIn is { IsSignedIn: false }) return;
        if (await OpenFromServerAsync())
        {
            FitToContent();
            await Bridge.PushViewportAsync();
        }

        _ = ResolveLinksAsync(includeStale: true);

        StateHasChanged();
    });

    private static async Task Step(ILogger log, string name, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Canvas startup step {Step} failed; continuing.", name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Store.OnChange -= OnStoreChanged;
        Viewport.OnChanged -= OnViewportChanged;
        Bridge.EditRequested -= OnEditRequested;
        Bridge.RenameGroupRequested -= OnRenameGroupRequested;
        Bridge.ContextMenuRequested -= OnContextMenuRequested;
        Documents.Changed -= OnDocumentsChanged;
        Documents.StorageRefused -= OnStorageRefused;
        Documents.PersistenceNotGranted -= OnPersistenceNotGranted;
        Paste.Problems -= OnPasteProblems;
        Paste.PermissionDenied -= OnPasteDenied;
        Paste.Placed -= OnPastePlaced;
        Layout.Changed -= OnLayoutChanged;
        ServerSession.Changed -= OnServerChanged;
        Access.Changed -= OnServerChanged;
        if (SignIn is not null) SignIn.Changed -= OnSignInChanged;

        try { await Documents.FlushAsync(); } catch (Exception) { }
        try { await Bridge.DetachAsync(); } catch (Exception) { }
        try { await Keyboard.UnregisterAsync(); } catch (Exception) { }
    }
}
