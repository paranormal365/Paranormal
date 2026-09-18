using Ben.Canvas.Core.Templates;
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

        // 7c: the template, again. The host learns it from the URL fragment, and reading that fragment
        // means exchanging a sign-in code with the API - a round trip that can finish AFTER this
        // startup began. Asking once more now that everything is up closes the race described on
        // SettleTemplateAsync; it is a no-op when the question is already settled.
        if (await SettleTemplateAsync())
        {
            FitToContent();
            await Bridge.PushViewportAsync();
            StateHasChanged();
        }

        // 7b: link cards on the board that never got a preview, or only a host a day ago.
        _ = ResolveLinksAsync(includeStale: true);

        // 8: after startup and never awaited, so a slow file listing cannot hold up the board.
        _ = Step(log, "sweep", () => Documents.SweepUnusedAssetsAsync());
    }

    /// <summary>Reopens the last board and its view, or starts a new board.</summary>
    private async Task RestoreAsync()
    {
        // The picker belongs to the editor, which owns the dialog; BoardLinks only asks for it.
        Boards.Picker = nodeId =>
        {
            _boardLinkNodeId = nodeId;
            _caseBoardsOpen = BoardCaseId is not null;
            StateHasChanged();
            return Task.CompletedTask;
        };

        var applied = await SettleTemplateAsync();

        var restored = false;
        string? problem = null;

        if (!applied)
        {
            (restored, problem) = await Documents.RestoreLastActiveAsync();
            if (!restored) Documents.New(CaseId);
            if (problem is not null) Toasts.Warning(CanvasCopy.Sentences.RestoreFailed(problem));
        }

        // The server comes after the device copy, so a slow or failed request still leaves a board open (M6).
        var opened = await OpenFromServerAsync();

        if ((restored || opened) && await Documents.LoadViewAsync() is { } view) Viewport.Set(view);
        else if (opened || applied) FitToContent();
        await Bridge.PushViewportAsync();
    }

    /// <summary>What was last opened from the server, so a re-render or a second sign-in event does not open it again.</summary>
    private (Guid?, Guid?)? _serverOpened;

    /// <summary>
    /// The template this load actually laid out, or null. Held past the restore because the case's own
    /// newest board is opened afterwards, and it must not replace a board somebody just chose.
    /// </summary>
    private string? _templateApplied;

    /// <summary>Whether the template question has been answered for this load, one way or the other.</summary>
    private bool _templateSettled;

    /// <summary>
    /// Lays out the template the host asked for, if that has not already been settled.
    /// </summary>
    /// <returns>True when a template was laid out by THIS call.</returns>
    /// <remarks>
    /// <para><b>Why this is a method and not simply part of the restore.</b> The site hands the editor
    /// a sign-in code, a case and a template in one URL fragment, and the host must exchange that code
    /// with the API before it can pass any of it down — an HTTP round trip. Blazor renders at the first
    /// await, so this component's startup can begin BEFORE the fragment has been read, with every
    /// parameter still null. The case already coped with arriving late; the template did not, and the
    /// result was a RACE: the load either laid out the template or opened the case's newest board
    /// instead, which looks exactly like the click having done nothing.</para>
    ///
    /// <para>Found by the e2e on 2026-09-18, which failed on the deck and passed on the family tree in
    /// the same run — the clearest possible sign of a race, and the reason a browser test earned its
    /// place here: nothing below this level could see it.</para>
    ///
    /// <para><b>A board asked for by id settles the question too</b>, without laying anything out. That
    /// is the same rule <c>BoardOpenPolicy</c> holds, applied a second time here so a template arriving
    /// after the board it lost to cannot still replace it.</para>
    /// </remarks>
    private async Task<bool> SettleTemplateAsync()
    {
        if (_templateSettled) return false;

        if (OpenServerDocumentId is not null)
        {
            _templateSettled = true;
            return false;
        }

        var plan = BoardOpenPolicy.For(OpenServerDocumentId, TemplateId);

        // Nothing asked for YET. Deliberately not settled: the fragment may still be on its way.
        if (plan.Template is null) return false;

        _templateSettled = true;
        _templateApplied = plan.Template;

        Documents.New(CaseId, plan.Template);

        // Stored at once, unlike a blank board. A template already holds what the person asked for, so
        // closing the tab before typing must not hand them a blank board the next time they open the
        // editor.
        await Documents.SaveAsync();

        if (plan.SayUnknownTemplate is { } unknown)
            Toasts.Warning(CanvasCopy.Sentences.UnknownTemplate(unknown));

        return true;
    }

    /// <summary>
    /// The host usually learns the case, the board and the template (from the site's hand-off) after the
    /// editor has started, so anything that arrives later is acted on then.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (!_ready) return;
        if (OpenServerDocumentId is null && CaseId is null && TemplateId is null) return;

        _ = InvokeAsync(async () =>
        {
            // The template first, always: it is what decides whether the case's own newest board may
            // replace what is open.
            if (await SettleTemplateAsync())
            {
                FitToContent();
                await Bridge.PushViewportAsync();
                StateHasChanged();
            }

            if (OpenServerDocumentId is not null || CaseId is not null) await CatchUpAsync();
        });
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
            : await ServerSession.OpenForCaseAsync(CaseId!.Value, OrganizationId, keepWhatIsOpen: _templateApplied is not null);

        Documents.SetOrganization(OrganizationId);
        if (ServerSession.PendingConflict is not null) _conflictOpen = true;
        else if (problem is not null) Toasts.Warning(problem);

        return Documents.CurrentServerId is not null && Documents.CurrentServerId != before;
    }

    /// <summary>Signing in after the editor started (another page, or the chip) opens the case's board then.</summary>
    private void OnSignInChanged() => _ = InvokeAsync(CatchUpAsync);

    private async Task CatchUpAsync()
    {
        if (!_ready || SignIn is { IsSignedIn: false }) return;
        if (await OpenFromServerAsync())
        {
            FitToContent();
            await Bridge.PushViewportAsync();
        }

        _ = ResolveLinksAsync(includeStale: true);

        StateHasChanged();
    }

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
        Presentation.Changed -= OnPresentationChanged;
        if (SignIn is not null) SignIn.Changed -= OnSignInChanged;

        try { await Documents.FlushAsync(); } catch (Exception) { }
        try { await Bridge.DetachAsync(); } catch (Exception) { }
        try { await Keyboard.UnregisterAsync(); } catch (Exception) { }
    }
}
