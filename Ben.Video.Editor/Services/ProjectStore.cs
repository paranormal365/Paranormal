using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Scoped service that manages a named list of saved projects stored in browser
/// <c>localStorage</c>.
///
/// <list type="bullet">
///   <item>Project index — stored under <c>bv-proj-index</c> as a JSON array of
///         <see cref="ProjectSummary"/>.</item>
///   <item>Full project JSON — stored under <c>bv-proj-{id}</c>.</item>
///   <item>Last-active project id — stored under <c>bv-proj-active</c>, consulted by
///         <see cref="RestoreLastActiveAsync"/> (item #69 fix). Without this, <see cref="SaveAsync"/>
///         reporting success gave no guarantee a reload would ever bring the project back — nothing
///         called <see cref="OpenAsync"/> automatically, on any host app.</item>
/// </list>
///
/// When a project is opened, source files are automatically re-loaded from OPFS
/// (via <see cref="OPFSService"/>) into ffmpeg MEMFS (via <see cref="FfmpegService"/>),
/// so the user does not need to manually re-import files that were previously persisted.
///
/// <see cref="IsDirty"/> is set on every <see cref="ClipStore.OnChange"/> event and
/// cleared on <see cref="SaveAsync"/> or <see cref="OpenAsync"/>.
/// </summary>
public sealed class ProjectStore : IAsyncDisposable
{
    private readonly ClipStore      _clips;
    private readonly ProjectService _projectService;
    private readonly OPFSService    _opfs;
    private readonly FfmpegService  _ffmpeg;
    private readonly SourceMounter  _mounter;
    private readonly IJSRuntime     _js;
    private readonly ErrorLogService _errorLog;
    private readonly MotionKeyframeService _motion;

    // Audit #4 — typed localStorage access instead of interpolated eval strings. Imported lazily
    // and cached: ProjectStore is constructed during DI setup, long before JS interop is legal.
    private IJSObjectReference? _storage;

    private async Task<IJSObjectReference> StorageAsync() =>
        _storage ??= await _js.InvokeAsync<IJSObjectReference>(
            "benImportEditorModule", "js/storageInterop.js");

    private const string IndexKey   = "bv-proj-index";
    private const string EntryPrefix = "bv-proj-";
    private const string ActiveKey  = "bv-proj-active";

    // See ProjectSerializer: one settings object for everything that reads or writes a project.
    private static JsonSerializerOptions _jsonOpts => ProjectSerializer.Options;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>In-memory cache of the project index (reflects localStorage).</summary>
    public List<ProjectSummary> Projects { get; private set; } = [];

    /// <summary>Name of the currently active project.</summary>
    public string CurrentProjectName { get; set; } = DefaultName();

    /// <summary>
    /// ID of the currently loaded project, or <c>null</c> when no saved project is open.
    /// </summary>
    public Guid? CurrentProjectId { get; private set; }

    /// <summary>
    /// <c>true</c> when the editor state has changed since the last save or open.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>Fires whenever <see cref="IsDirty"/> or <see cref="Projects"/> changes.</summary>
    public event Action? OnChanged;

    // Signals when InitAsync has completed so callers can safely restore a project.
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ── Construction ──────────────────────────────────────────────────────────

    public ProjectStore(ClipStore clips, ProjectService projectService,
        OPFSService opfs, FfmpegService ffmpeg, SourceMounter mounter,
        MotionKeyframeService motion, IJSRuntime js, ErrorLogService errorLog)
    {
        _clips          = clips;
        _projectService = projectService;
        _opfs           = opfs;
        _ffmpeg         = ffmpeg;
        _mounter        = mounter;
        _motion         = motion;
        _js             = js;
        _errorLog       = errorLog;

        _clips.OnChange += OnClipsChanged;

        // Animating a layer is editing the project. Motion paths live in their own service, and
        // nothing connected its changes to the dirty flag — so an afternoon spent on keyframes and
        // nothing else left the project looking saved, with no prompt on the way out and nothing
        // for autosave to write (2026-09-05 audit, persistence-11).
        _motion.OnChanged += OnClipsChanged;
    }

    private void OnClipsChanged()
    {
        IsDirty = true;
        OnChanged?.Invoke();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    /// <summary>Load the project index from localStorage. Call once during app startup.</summary>
    public async Task InitAsync()
    {
        try
        {
            var json = await (await StorageAsync()).InvokeAsync<string?>("getItem", IndexKey);
            if (!string.IsNullOrEmpty(json))
                Projects = JsonSerializer.Deserialize<List<ProjectSummary>>(json, _jsonOpts) ?? [];
        }
        catch (Exception ex)
        {
            _errorLog.Log("ProjectStore.InitAsync", ex);
        }
        finally
        {
            _ready.TrySetResult();
        }
    }

    // ── Load from server record ───────────────────────────────────────────────

    /// <summary>
    /// Restores a <see cref="ProjectFile"/> obtained from the server into the editor without
    /// touching localStorage. Fires <see cref="OnChanged"/> so the editor re-renders.
    /// </summary>
    public async Task LoadFromFileAsync(ProjectFile file, string name, Guid? serverId = null)
    {
        await _ready.Task; // wait for InitAsync if called before the editor finishes startup
        _projectService.RestoreAsync(file);
        CurrentProjectName = name;
        CurrentProjectId   = serverId;
        IsDirty            = false;
        OnChanged?.Invoke();
        _ = Task.Run(async () => await RestoreOpfsFilesAsync(file));
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialise the current editor state and save to localStorage.
    /// Updates the project index with a new or updated entry.
    /// </summary>
    /// <param name="name">Project name (defaults to <see cref="CurrentProjectName"/>).</param>
    public async Task SaveAsync(string? name = null)
    {
        var projectName = name?.Trim() is { Length: > 0 } n ? n : CurrentProjectName;
        CurrentProjectName = projectName;

        var id  = CurrentProjectId ?? Guid.NewGuid();
        CurrentProjectId = id;

        try
        {
            // Build project JSON via ProjectService internals
            var file = BuildCurrentProjectFile(projectName);
            var json = JsonSerializer.Serialize(file, _jsonOpts);
            var sizeBytes = Encoding.UTF8.GetByteCount(json);

            // Store to localStorage
            // The result is the point: the browser reports a refused write rather than throwing,
            // and this used to discard it and report success over the top (2026-09-05 audit,
            // persistence-9).
            var stored = await (await StorageAsync()).InvokeAsync<bool>("setItem", $"{EntryPrefix}{id}", json);
            if (!stored) throw ProjectStorageException.WriteRefused($"“{projectName}”");

            // Update index
            var existing = Projects.FirstOrDefault(p => p.Id == id);
            if (existing is not null)
            {
                existing.Name      = projectName;
                existing.UpdatedAt = DateTime.Now;
                existing.SizeBytes = sizeBytes;
            }
            else
            {
                Projects.Add(new ProjectSummary
                {
                    Id        = id,
                    Name      = projectName,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    SizeBytes = sizeBytes,
                });
            }

            await PersistIndexAsync();
            await PersistActiveIdAsync(id);
            IsDirty = false;
            OnChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _errorLog.Log("ProjectStore.SaveAsync", ex);
            throw;
        }
    }

    // ── Open ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Load a saved project from localStorage and restore it into the editor.
    /// For each clip that has an <c>OpfsExt</c>, the source file is automatically
    /// re-loaded from OPFS into ffmpeg MEMFS.
    /// </summary>
    public async Task OpenAsync(Guid id)
    {
        try
        {
            var json = await (await StorageAsync()).InvokeAsync<string?>("getItem", $"{EntryPrefix}{id}");
            if (string.IsNullOrEmpty(json)) return;

            var file = JsonSerializer.Deserialize<ProjectFile>(json, _jsonOpts);
            if (file is null) return;

            _projectService.RestoreAsync(file);

            var summary = Projects.FirstOrDefault(p => p.Id == id);
            CurrentProjectName = summary?.Name ?? file.ProjectName ?? DefaultName();
            CurrentProjectId   = id;
            IsDirty            = false;
            await PersistActiveIdAsync(id);
            OnChanged?.Invoke();

            // Re-load OPFS source files into MEMFS in the background
            _ = Task.Run(async () => await RestoreOpfsFilesAsync(file));
        }
        catch (Exception ex)
        {
            _errorLog.Log("ProjectStore.OpenAsync", ex);
            throw;
        }
    }

    /// <summary>
    /// Item #69 fix — re-opens whichever project was last active (per <see cref="PersistActiveIdAsync"/>),
    /// if any, into the editor. Call once during app startup, after <see cref="InitAsync"/>, so a
    /// page reload restores the user's in-progress work instead of silently discarding it despite
    /// a prior successful <see cref="SaveAsync"/>. A missing/unreadable pointer, or a pointer to a
    /// project that no longer exists in the index (e.g. deleted from another tab), is treated as
    /// "nothing to restore" rather than an error — this is a best-effort convenience, not a
    /// contract the caller should treat as guaranteed to succeed.
    /// </summary>
    public async Task RestoreLastActiveAsync()
    {
        try
        {
            var raw = await (await StorageAsync()).InvokeAsync<string?>("getItem", ActiveKey);
            if (string.IsNullOrEmpty(raw) || !Guid.TryParse(raw, out var id)) return;
            if (Projects.All(p => p.Id != id)) return; // stale pointer — project no longer exists

            await OpenAsync(id);
        }
        catch (Exception ex)
        {
            _errorLog.Log("ProjectStore.RestoreLastActiveAsync", ex);
        }
    }

    // ── Rename ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rename a saved project and persist the change to the localStorage index.
    /// No-op if <paramref name="id"/> isn't found or <paramref name="newName"/> is blank.
    /// </summary>
    public async Task RenameAsync(Guid id, string newName)
    {
        var trimmed = newName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        var summary = Projects.FirstOrDefault(p => p.Id == id);
        if (summary is null || summary.Name == trimmed) return;

        summary.Name = trimmed;
        if (CurrentProjectId == id) CurrentProjectName = trimmed;

        try
        {
            await PersistIndexAsync();
            OnChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _errorLog.Log("ProjectStore.RenameAsync", ex); // audit #6 — see DeleteAsync's note
            throw;
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remove a saved project from the index and localStorage.
    /// OPFS source files are NOT removed (they may be referenced by other projects).
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        try
        {
            await (await StorageAsync()).InvokeVoidAsync("removeItem", $"{EntryPrefix}{id}");
            Projects.RemoveAll(p => p.Id == id);
            await PersistIndexAsync();
            // Item #69 — an active-pointer left dangling at a just-deleted id would otherwise
            // make the NEXT reload's restore silently no-op forever (RestoreLastActiveAsync
            // treats a stale pointer as "nothing to restore").
            if (CurrentProjectId == id) await PersistActiveIdAsync(null);
            OnChanged?.Invoke();
        }
        catch (Exception ex)
        {
            // Audit #6 — this used to swallow, so a failed delete left the project listed with no
            // feedback at all. SaveAsync/OpenAsync already rethrow so their callers can surface the
            // failure; these now match rather than being the odd ones out.
            _errorLog.Log("ProjectStore.DeleteAsync", ex);
            throw;
        }
    }

    // ── New project ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reset state for a new project (clear dirty flag and assign default name).
    /// Does NOT clear the ClipStore — the caller is responsible for that.
    /// </summary>
    public async Task NewProjectAsync()
    {
        CurrentProjectId   = null;
        CurrentProjectName = DefaultName();
        IsDirty            = false;
        await PersistActiveIdAsync(null); // clears the restore-on-reload pointer
        OnChanged?.Invoke();
    }

    // ── OPFS restore ──────────────────────────────────────────────────────────

    private async Task RestoreOpfsFilesAsync(ProjectFile file)
    {
        // Item #69 fix — every OTHER OPFSService caller in this codebase calls EnsureInitAsync()
        // itself before touching IsAvailable; this was the one exception, checking the flag
        // upfront without it. Harmless before now, since a manual File > Open only ever happened
        // after some earlier OPFS operation (e.g. an import) had already initialized it as a side
        // effect. Restore-on-startup breaks that assumption — on a fresh reload with no prior
        // action, IsAvailable was still its default false, so this returned immediately and no
        // clip's media (nor MEMFS content) was ever actually restored, only its metadata.
        await _opfs.EnsureInitAsync();
        if (!_opfs.IsAvailable) return;
        // Wait for ffmpeg to be ready before writing to MEMFS
        var waited = 0;
        while (_ffmpeg.State != FfmpegState.Ready && waited < 30_000)
        {
            await Task.Delay(500);
            waited += 500;
        }
        if (_ffmpeg.State != FfmpegState.Ready)
        {
            // Phase 143: this used to return here in total silence — a project could open with
            // every clip missing its MEMFS source and nothing anywhere explained why (ffmpeg
            // simply never became Ready inside the 30s window, e.g. stuck loading or wedged).
            _errorLog.Log("ProjectStore.RestoreOpfsFilesAsync",
                $"clips not restored — ffmpeg did not become Ready within {waited / 1000}s. " +
                "Re-open this project once ffmpeg finishes loading (or reset it if it appears stuck).");
            return;
        }

        foreach (var track in file.Tracks)
        {
            foreach (var vc in track.VideoClips.Where(c => c.OpfsExt is not null))
            {
                try
                {
                    var clip = _clips.AllVideoClips.FirstOrDefault(c => c.Id == vc.Id);
                    if (clip is null) continue;
                    var memFsName = await RestoreOneAsync(vc.Id, vc.OpfsExt!);
                    if (memFsName is null) continue;
                    clip.MemFsName      = memFsName;
                    clip.IsMediaMissing = false;
                }
                catch (Exception ex) { _errorLog.Log("ProjectStore.RestoreOpfs(video)", ex); }
            }

            foreach (var ac in track.AudioClips.Where(c => c.OpfsExt is not null))
            {
                try
                {
                    var clip = _clips.AllAudioClips.FirstOrDefault(c => c.Id == ac.Id);
                    if (clip is null) continue;
                    var memFsName = await RestoreOneAsync(ac.Id, ac.OpfsExt!);
                    if (memFsName is null) continue;
                    clip.MemFsName      = memFsName;
                    clip.IsMediaMissing = false;
                }
                catch (Exception ex) { _errorLog.Log("ProjectStore.RestoreOpfs(audio)", ex); }
            }

            foreach (var ic in track.ImageClips.Where(c => c.OpfsExt is not null))
            {
                try
                {
                    var clip = _clips.AllImageClips.FirstOrDefault(c => c.Id == ic.Id);
                    if (clip is null) continue;
                    var memFsName = await RestoreOneAsync(ic.Id, ic.OpfsExt!);
                    if (memFsName is null) continue;
                    clip.MemFsName      = memFsName;
                    clip.IsMediaMissing = false;
                }
                catch (Exception ex) { _errorLog.Log("ProjectStore.RestoreOpfs(image)", ex); }
            }
        }

        // Pre-existing gap, surfaced by item #69's fix: nothing above notifies the ClipStore
        // after flipping each clip's IsMediaMissing to false, so the timeline's "media missing"
        // warning chip stayed stale until some unrelated re-render happened to clear it. Only
        // reachable via a manual File > Open before now — the restore-on-startup path this phase
        // adds makes it visible on every single restore, so a one-time notify once the whole
        // batch finishes is worth doing here rather than leaving a fixed project looking broken.
        _clips.NotifyChanged();
    }

    /// <summary>
    /// Item #38 phase B — restores one clip's OPFS copy as a zero-copy WORKERFS mount instead of a
    /// full MEMFS byte copy (the pre-phase-B behavior). Falls back to a real copy when mounting
    /// fails for any reason (e.g. this browser's OPFS doesn't support the underlying mount
    /// mechanics), so a project always finishes restoring even on a browser where mounting isn't
    /// available — just without the memory win.
    /// </summary>
    private async Task<string?> RestoreOneAsync(Guid clipId, string opfsExt)
    {
        var mounted = await _mounter.MountAsync(clipId, opfsExt);
        if (mounted is not null) return mounted;

        var jsFile = await _opfs.ReadAsJSFileAsync(clipId, opfsExt);
        if (jsFile is null) return null;
        var memFsName = $"{clipId}{opfsExt}";
        await _ffmpeg.WriteFileAsync(memFsName, jsFile);
        return memFsName;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task PersistIndexAsync()
    {
        // No double-encoding any more: the value crosses as a real argument, so JSON that
        // happens to contain quotes/backslashes (project names are user-typed) can't reshape a
        // script string, because there is no script string.
        var json = JsonSerializer.Serialize(Projects, _jsonOpts);
        var stored = await (await StorageAsync()).InvokeAsync<bool>("setItem", IndexKey, json);
        if (!stored) throw ProjectStorageException.WriteRefused("the list of projects");
    }

    /// <summary>Item #69 fix — records (or clears) which project id <see cref="RestoreLastActiveAsync"/>
    /// should re-open on the next page load.</summary>
    private async Task PersistActiveIdAsync(Guid? id)
    {
        if (id is { } value)
            // Not fatal: this only records which project to reopen, so a refusal costs the
            // convenience and not the work.
            await (await StorageAsync()).InvokeAsync<bool>("setItem", ActiveKey, value.ToString());
        else
            await (await StorageAsync()).InvokeVoidAsync("removeItem", ActiveKey);
    }

    private ProjectFile BuildCurrentProjectFile(string name)
    {
        // Use ProjectService to serialize (it has access to the full ClipStore mapping)
        // We call the internal BuildProjectFile via the public SaveAsync path, but here
        // we need the raw file. Reuse by serializing through ProjectService's download path
        // — but instead of downloading, capture the bytes via a workaround by reading
        // the JSON that BuildProjectFile produces.
        // Simpler: delegate to ProjectService which already has all the mapping logic.
        return _projectService.BuildCurrentProjectFile(name);
    }

    private static string DefaultName()
        => $"Project {DateTime.Now:yyyy-MM-dd HH:mm}";

    /// <summary>Audit #4 — releases the cached storageInterop handle. Registered as a scoped
    /// service, so Blazor's DI container disposes this at circuit teardown; the guard covers the
    /// case where the circuit is already gone by then.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_storage is null) return;
        try { await _storage.DisposeAsync(); } catch (JSDisconnectedException) { } catch (ObjectDisposedException) { }
        _storage = null;
    }
}

/// <summary>Lightweight project index entry stored in the localStorage index.</summary>
public sealed class ProjectSummary
{
    public Guid     Id        { get; set; } = Guid.NewGuid();
    public string   Name      { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public long     SizeBytes { get; set; }

    public string FormattedSize => SizeBytes switch
    {
        < 1_024        => $"{SizeBytes} B",
        < 1_048_576    => $"{SizeBytes / 1_024.0:F1} KB",
        _              => $"{SizeBytes / 1_048_576.0:F1} MB",
    };

    public string FormattedDate => UpdatedAt.ToString("MMM d, yyyy h:mm tt");
}
