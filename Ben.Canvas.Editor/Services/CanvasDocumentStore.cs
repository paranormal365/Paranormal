using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Persistence;
using Ben.Canvas.Core.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ben.Canvas.Editor.Services;

/// <summary>
/// Keeps boards on this device: autosave, the board list, the open board and its view, and the sweep of
/// pictures nothing refers to any more.
/// </summary>
/// <remarks>
/// <para>Copied in shape from Ben.Video.Editor's ProjectStore, with its lessons kept:</para>
/// <list type="bullet">
/// <item>The device key (<see cref="CurrentLocalId"/>) is not the board's id and not the server's id. One
/// field for two meanings once made a board opened from the server overwrite an unrelated local board.</item>
/// <item>Autosave starts only after the last board has been restored, so restoring is never itself an edit
/// that rewrites what it just read.</item>
/// <item>A write the browser refused is reported, not counted as saved, and reported once.</item>
/// <item>A board list that could not be read makes every stored picture look unreferenced, so the sweep
/// refuses to run rather than delete them.</item>
/// </list>
/// <para>Autosave waits until nothing is being dragged: serialising mid-gesture would capture a half-moved
/// board and cost a frame while the pointer is down.</para>
/// </remarks>
public sealed class CanvasDocumentStore : IAsyncDisposable
{
    private readonly CanvasStore _store;
    private readonly CanvasViewportState _viewport;
    private readonly CanvasAssetStore _assets;
    private readonly IJSRuntime _js;
    private readonly ILogger<CanvasDocumentStore> _log;

    private IJSObjectReference? _module;
    private List<DocumentSummary> _documents = [];
    private bool _indexWasRead;
    private bool _restoring;
    private bool _autosaveEnabled;
    private CancellationTokenSource? _autosave;
    private CancellationTokenSource? _viewSave;
    private bool _refusalReported;

    public CanvasDocumentStore(CanvasStore store, CanvasViewportState viewport, CanvasAssetStore assets, IJSRuntime js, ILogger<CanvasDocumentStore> log)
    {
        _store = store;
        _viewport = viewport;
        _assets = assets;
        _js = js;
        _log = log;
        _store.OnChange += OnStoreChanged;
    }

    private const string PersistenceToldKey = "bc-persist-told";

    /// <summary>How long after the last edit the board is written. Tests shorten it.</summary>
    internal TimeSpan AutosaveIdle { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the view waits after the last pan or zoom before it is written.</summary>
    internal TimeSpan ViewIdle { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>A stored file younger than this is left alone by the sweep: another tab may not have saved it yet.</summary>
    internal TimeSpan SweepGrace { get; set; } = TimeSpan.FromHours(1);

    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public IReadOnlyList<DocumentSummary> Documents => _documents;

    /// <summary>The key the open board is stored under on this device, or null for a board never stored here.</summary>
    public Guid? CurrentLocalId { get; private set; }

    /// <summary>The server's id for the open board, once it has been saved to or opened from a case.</summary>
    public Guid? CurrentServerId { get; private set; }

    /// <summary>The organisation that owns the open board's case, when known; uploads go under it.</summary>
    public Guid? CurrentOrganizationId { get; private set; }

    /// <summary>True when the open board was edited after its last save to (or open from) the case.</summary>
    public bool ChangedSinceServer { get; private set; }

    /// <summary>The summary stored for the open board, or null before its first save on this device.</summary>
    public DocumentSummary? CurrentSummary => _documents.FirstOrDefault(d => d.LocalId == CurrentLocalId);

    public bool IsDirty { get; private set; }

    public bool AutosavePending => _autosave is { IsCancellationRequested: false };

    public SaveState State { get; private set; } = SaveState.Clean;

    /// <summary>True once the browser has refused a write in this session.</summary>
    public bool LocalWriteRefused { get; private set; }

    public event Action? Changed;

    /// <summary>Raised the first time a write is refused, so the editor can say so once.</summary>
    public event Action? StorageRefused;

    /// <summary>Raised when the browser will not promise to keep this site's storage.</summary>
    public event Action? PersistenceNotGranted;

    public async Task StartAsync()
    {
        _module = await CanvasModules.ImportAsync(_js, "js/storageInterop.js");
        var index = await _module.InvokeAsync<string?>("getItem", DocumentIndex.IndexKey);
        var parsed = DocumentIndex.Parse(index);
        _indexWasRead = parsed is not null;
        _documents = parsed ?? [];

        // Said once per device: a browser that will not promise to keep the boards is worth one sentence,
        // not a sentence on every visit.
        if (!await _module.InvokeAsync<bool>("persist")
            && await _module.InvokeAsync<string?>("getItem", PersistenceToldKey) is null)
        {
            await _module.InvokeAsync<bool>("setItem", PersistenceToldKey, "1");
            PersistenceNotGranted?.Invoke();
        }
    }

    /// <summary>Opens the board that was open last time. Returns a sentence when it exists but cannot be read.</summary>
    public async Task<(bool Restored, string? Problem)> RestoreLastActiveAsync()
    {
        if (_module is null) return (false, null);
        var active = await _module.InvokeAsync<string?>("getItem", DocumentIndex.ActiveKey);
        if (!Guid.TryParse(active, out var localId)) return (false, null);

        var text = await _module.InvokeAsync<string?>("docGet", DocumentIndex.EntryKey(localId));
        if (text is null)
        {
            await _module.InvokeAsync<bool>("removeItem", DocumentIndex.ActiveKey);
            return (false, null);
        }

        var (document, problem) = CanvasSerializer.Parse(text);
        if (document is null)
        {
            _log.LogWarning("The last board on this device could not be read: {Problem}", problem);
            return (false, problem);
        }

        LoadWithoutEditing(document);
        CurrentLocalId = localId;
        var restoredSummary = _documents.FirstOrDefault(d => d.LocalId == localId);
        CurrentServerId = restoredSummary?.ServerId;
        CurrentOrganizationId = restoredSummary?.OrganizationId;
        ChangedSinceServer = restoredSummary?.ChangedSinceServer ?? false;
        State = new SaveState(SaveStateKind.SavedLocal, restoredSummary?.UpdatedAtUtc);
        Changed?.Invoke();
        return (true, null);
    }

    /// <summary>Starts an empty board. It is not stored until its first edit.</summary>
    public void New(Guid? caseId)
    {
        LoadWithoutEditing(new CanvasDocument { CaseId = caseId });
        CurrentLocalId = Guid.NewGuid();
        CurrentServerId = null;
        ChangedSinceServer = false;
        State = SaveState.Clean;
        Changed?.Invoke();
    }

    /// <summary>Opens an imported board under a new device key; its own id is kept.</summary>
    public async Task<bool> AdoptImportedAsync(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LoadWithoutEditing(document);
        CurrentLocalId = Guid.NewGuid();
        // An imported board is a copy: saving it to a case makes a new server board, never overwrites one.
        CurrentServerId = null;
        document.Revision = 0;
        return await SaveAsync();
    }

    private void LoadWithoutEditing(CanvasDocument document)
    {
        CancelAutosave();
        _restoring = true;
        try
        {
            _store.Load(document);
        }
        finally
        {
            _restoring = false;
        }

        IsDirty = false;
    }

    public void EnableAutosave() => _autosaveEnabled = true;

    private void OnStoreChanged(CanvasChange change)
    {
        if (_restoring || change.Kind == CanvasChangeKind.Reset) return;
        IsDirty = true;
        ChangedSinceServer = true;
        if (State.Kind is not (SaveStateKind.Editing or SaveStateKind.ServerConflict)) State = new SaveState(SaveStateKind.Editing);
        if (_autosaveEnabled) ScheduleAutosave();
        Changed?.Invoke();
    }

    private void ScheduleAutosave()
    {
        CancelAutosave();
        var cts = new CancellationTokenSource();
        _autosave = cts;
        _ = RunAutosaveAsync(cts);
    }

    private void CancelAutosave()
    {
        _autosave?.Cancel();
        _autosave = null;
    }

    private async Task RunAutosaveAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(AutosaveIdle, cts.Token);
            while (_viewport.GestureActive || _store.GestureInProgress)
                await Task.Delay(250, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (ReferenceEquals(_autosave, cts)) _autosave = null;
            await SaveAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Autosave failed.");
        }
    }

    /// <summary>Writes the open board now. False when the browser refused the write.</summary>
    public async Task<bool> SaveAsync()
    {
        if (_module is null) return false;
        CancelAutosave();
        CurrentLocalId ??= Guid.NewGuid();
        var localId = CurrentLocalId.Value;
        var document = _store.Document;
        var now = UtcNow();

        State = new SaveState(SaveStateKind.SavingLocal);
        Changed?.Invoke();

        document.SavedAtUtc = now;
        var json = CanvasSerializer.Serialize(document, compact: true);
        var written = await _module.InvokeAsync<bool>("docPut", DocumentIndex.EntryKey(localId), json);
        if (!written)
        {
            LocalWriteRefused = true;
            State = new SaveState(SaveStateKind.Editing, Problem: Core.Text.CanvasCopy.Sentences.StorageRefused);
            Changed?.Invoke();
            if (!_refusalReported)
            {
                _refusalReported = true;
                StorageRefused?.Invoke();
            }

            return false;
        }

        var summary = _documents.FirstOrDefault(d => d.LocalId == localId);
        if (summary is null)
        {
            summary = new DocumentSummary { LocalId = localId, CreatedAtUtc = document.CreatedAtUtc };
            _documents.Add(summary);
        }

        summary.DocumentId = document.Id;
        summary.Title = document.Title;
        summary.CaseId = document.CaseId;
        summary.OrganizationId = CurrentOrganizationId ?? summary.OrganizationId;
        summary.ServerId = CurrentServerId;
        summary.ServerRevision = document.Revision;
        summary.ChangedSinceServer = ChangedSinceServer;
        summary.UpdatedAtUtc = now;
        summary.SizeBytes = json.Length;
        summary.AssetIds = document.ReferencedAssetIds().ToList();

        await _module.InvokeAsync<bool>("setItem", DocumentIndex.IndexKey, DocumentIndex.Serialise(_documents));
        await _module.InvokeAsync<bool>("setItem", DocumentIndex.ActiveKey, localId.ToString("D"));

        IsDirty = false;
        State = new SaveState(SaveStateKind.SavedLocal, now);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Shows a server step (saving, saved, conflict, failed) in the header.</summary>
    public void SetState(SaveState state)
    {
        State = state;
        Changed?.Invoke();
    }

    /// <summary>The organisation the open board's case belongs to, as the host or the server said.</summary>
    public void SetOrganization(Guid? organizationId)
    {
        if (organizationId is not null) CurrentOrganizationId = organizationId;
    }

    /// <summary>
    /// Records a save the server accepted: its id and revision become the board's, the device copy is rewritten,
    /// and the header says "Saved to case". Not an edit, so nothing is added to undo.
    /// </summary>
    public async Task<bool> RecordServerSaveAsync(Guid serverId, int revision, Guid? caseId, Guid? organizationId)
    {
        var document = _store.Document;
        document.Revision = revision;
        document.CaseId = caseId ?? document.CaseId;
        CurrentServerId = serverId;
        SetOrganization(organizationId);
        ChangedSinceServer = false;

        var written = await SaveAsync();
        State = new SaveState(SaveStateKind.SavedServer, UtcNow());
        Changed?.Invoke();
        return written;
    }

    /// <summary>
    /// Opens a board read from the server. A copy already on this device for the same server board is replaced
    /// under its own key, so opening a board twice never leaves two copies. Undo starts empty.
    /// </summary>
    public async Task<bool> OpenFromServerAsync(CanvasDocument document, Guid serverId, int revision, Guid? organizationId)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Revision = revision;
        var existing = _documents.FirstOrDefault(d => d.ServerId == serverId);

        LoadWithoutEditing(document);
        CurrentLocalId = existing?.LocalId ?? Guid.NewGuid();
        CurrentServerId = serverId;
        SetOrganization(organizationId ?? existing?.OrganizationId);
        ChangedSinceServer = false;

        var written = await SaveAsync();
        State = new SaveState(SaveStateKind.SavedServer, UtcNow());
        Changed?.Invoke();
        return written;
    }

    /// <summary>Forgets the server board this one was saved as (it is gone there), so the next save creates a new one.</summary>
    public void DetachFromServer()
    {
        CurrentServerId = null;
        _store.Document.Revision = 0;
        ChangedSinceServer = true;
        Changed?.Invoke();
    }

    /// <summary>The device copy of a server board, if one is stored here.</summary>
    public DocumentSummary? FindByServerId(Guid serverId) => _documents.FirstOrDefault(d => d.ServerId == serverId);

    /// <summary>Opens a board stored on this device by its key; a sentence when it cannot be read.</summary>
    public async Task<string?> OpenLocalAsync(Guid localId)
    {
        if (_module is null) return null;
        var text = await _module.InvokeAsync<string?>("docGet", DocumentIndex.EntryKey(localId));
        if (text is null) return Core.Text.CanvasCopy.Sentences.FileEmpty;
        var (document, problem) = CanvasSerializer.Parse(text);
        if (document is null) return problem;

        var summary = _documents.FirstOrDefault(d => d.LocalId == localId);
        LoadWithoutEditing(document);
        CurrentLocalId = localId;
        CurrentServerId = summary?.ServerId;
        CurrentOrganizationId = summary?.OrganizationId;
        ChangedSinceServer = summary?.ChangedSinceServer ?? false;
        await _module.InvokeAsync<bool>("setItem", DocumentIndex.ActiveKey, localId.ToString("D"));
        State = new SaveState(SaveStateKind.SavedLocal, summary?.UpdatedAtUtc);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Writes a pending autosave straight away; called when the page is hidden.</summary>
    public async Task FlushAsync()
    {
        if (IsDirty || AutosavePending) await SaveAsync();
    }

    public void SaveViewSoon(CanvasViewport viewport)
    {
        if (_module is null || CurrentLocalId is not { } localId) return;
        _viewSave?.Cancel();
        var cts = new CancellationTokenSource();
        _viewSave = cts;
        _ = SaveViewAsync(localId, viewport, cts.Token);
    }

    private async Task SaveViewAsync(Guid localId, CanvasViewport viewport, CancellationToken token)
    {
        try
        {
            await Task.Delay(ViewIdle, token);
            await _module!.InvokeAsync<bool>("setItem", ViewSnapshot.StorageKeyPrefix + localId.ToString("D"), ViewSnapshot.Serialise(viewport));
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSException ex)
        {
            _log.LogWarning(ex, "Could not store the board view.");
        }
    }

    public async Task<CanvasViewport?> LoadViewAsync()
    {
        if (_module is null || CurrentLocalId is not { } localId) return null;
        var text = await _module.InvokeAsync<string?>("getItem", ViewSnapshot.StorageKeyPrefix + localId.ToString("D"));
        return ViewSnapshot.Deserialise(text)?.Apply();
    }

    public async Task DeleteAsync(Guid localId)
    {
        if (_module is null) return;
        await _module.InvokeAsync<bool>("docDelete", DocumentIndex.EntryKey(localId));
        await _module.InvokeAsync<bool>("removeItem", ViewSnapshot.StorageKeyPrefix + localId.ToString("D"));
        _documents.RemoveAll(d => d.LocalId == localId);
        await _module.InvokeAsync<bool>("setItem", DocumentIndex.IndexKey, DocumentIndex.Serialise(_documents));
        var active = await _module.InvokeAsync<string?>("getItem", DocumentIndex.ActiveKey);
        if (Guid.TryParse(active, out var id) && id == localId)
            await _module.InvokeAsync<bool>("removeItem", DocumentIndex.ActiveKey);
        Changed?.Invoke();
    }

    /// <summary>
    /// Deletes stored pictures and files that no stored board, the open board, or its undo history names.
    /// </summary>
    /// <returns>How many files were deleted.</returns>
    public async Task<int> SweepUnusedAssetsAsync()
    {
        if (_module is null || !_assets.IsAvailable) return 0;

        // Another tab may have saved a board since this one started.
        var fresh = DocumentIndex.Parse(await _module.InvokeAsync<string?>("getItem", DocumentIndex.IndexKey));
        if (fresh is null) return 0;

        var stored = await _assets.ListAsync();
        if (!AssetGarbageCollector.CanSweep(_indexWasRead, fresh.Count + (IsDirty ? 1 : 0), stored.Count)) return 0;

        var referenced = fresh.SelectMany(d => d.AssetIds)
            .Concat(_store.Document.ReferencedAssetIds())
            .Concat(_store.AssetIdsHeldByHistory());

        var cutoff = new DateTimeOffset(UtcNow() - SweepGrace).ToUnixTimeMilliseconds();
        var old = stored.Where(a => a.Modified < cutoff).ToList();
        var orphans = AssetGarbageCollector.FindOrphans(old.Select(a => a.AssetId), referenced).ToHashSet();

        var deleted = 0;
        foreach (var asset in old.Where(a => orphans.Contains(a.AssetId)))
            if (await _assets.DeleteAsync(asset.AssetId, asset.Ext)) deleted++;

        if (deleted > 0) _log.LogInformation("Swept {Count} unused pictures and files from this device.", deleted);
        return deleted;
    }

    public async ValueTask DisposeAsync()
    {
        _store.OnChange -= OnStoreChanged;
        CancelAutosave();
        _viewSave?.Cancel();
        if (_module is null) return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or ObjectDisposedException) { }
    }
}
