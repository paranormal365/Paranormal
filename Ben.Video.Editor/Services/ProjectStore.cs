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

    /// <summary>Re-fetching missing media from the server, when the host can.</summary>
    /// <remarks>Optional: a host with no media library has nothing to fetch from.</remarks>
    private readonly MediaRelinkService? _relink;
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
    /// The key this project is stored under in this browser, or null when it has never been saved
    /// here.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately not the same thing as <see cref="CurrentServerId"/>. There used to be one
    /// field for both, and opening a project from the server set it to the server's row id — so
    /// the next local save wrote to browser storage under a key from the server's namespace. Two
    /// unrelated identifiers sharing a field is the kind of thing that works until the day two
    /// systems disagree about what a Guid means (2026-09-05 audit, F15).</para>
    ///
    /// <para>Opening a server project leaves this null on purpose: pressing Save then keeps a
    /// local copy under a key of its own rather than pretending the server's row lives here.</para>
    /// </remarks>
    public Guid? CurrentLocalId { get; private set; }

    /// <summary>
    /// The server row this project came from or was saved to, or null when it exists only here.
    /// </summary>
    /// <remarks>
    /// What a publish attaches its video to, and what a save-to-server updates instead of
    /// creating a second row (2026-09-05 audit, persistence-13 and site-4).
    /// </remarks>
    public Guid? CurrentServerId { get; set; }

    /// <summary>
    /// A server project the host asked for before the editor was up, opened once it is.
    /// </summary>
    /// <remarks>
    /// <para>The standalone editor is reached by a link that can name a project (phase 12), and
    /// the link is followed long before the editor has restored anything. Loading it there and
    /// then would be overwritten moments later by <see cref="RestoreLastActiveAsync"/>, which runs
    /// on first render — so the host leaves the request here and the editor honours it after the
    /// restore, when it is the last word rather than the first.</para>
    ///
    /// <para>Cleared once acted on, so a re-render never opens it twice.</para>
    /// </remarks>
    public Guid? PendingServerProjectId { get; set; }

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
        MotionKeyframeService motion, IJSRuntime js, ErrorLogService errorLog,
        MediaRelinkService? relink = null)
    {
        _relink         = relink;
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
        // Restoring a project is not editing it: the store raises change events as it rebuilds the
        // timeline, and treating those as edits would mark a freshly-opened project dirty and set
        // autosave writing it straight back.
        if (_restoring) return;

        IsDirty = true;
        OnChanged?.Invoke();
        ScheduleAutosave();
    }

    // ── Housekeeping ──────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes stored media that no saved project, and nothing currently open, refers to.
    /// </summary>
    /// <returns>How many files were removed, and how many bytes that freed.</returns>
    /// <remarks>
    /// <para>Nothing ever freed a source. Every import writes a copy of the file into the browser's
    /// own storage so the project can be reopened, and removing the clip, deleting the project, or
    /// simply closing the tab left that copy behind forever. A few sessions with large footage fill
    /// the quota, at which point saving starts failing (2026-09-05 audit, media-2 and
    /// persistence-12).</para>
    ///
    /// <para>Reconciles against every saved project rather than counting references, and refuses to
    /// run at all when the project list could not be read — see
    /// <see cref="OpfsGarbageCollector.CanSweep"/>, because a failure to read the index makes every
    /// file look unreferenced.</para>
    /// </remarks>
    public async Task<(int Files, long Bytes)> SweepUnusedMediaAsync()
    {
        var stored = await _opfs.ListClipsAsync();
        if (stored.Count == 0) return (0, 0);

        if (!OpfsGarbageCollector.CanSweep(_indexWasRead, Projects.Count, stored.Count))
        {
            _errorLog.Log("ProjectStore.SweepUnusedMediaAsync",
                $"Refused to sweep: {stored.Count} stored file(s) with "
                + $"{Projects.Count} known project(s) and index read = {_indexWasRead}.");
            return (0, 0);
        }

        var referenced = await CollectReferencedClipIdsAsync();

        var byId = stored
            .Where(e => Guid.TryParse(e.ClipId, out _))
            .ToDictionary(e => Guid.Parse(e.ClipId));

        var orphans = OpfsGarbageCollector.FindOrphans(byId.Keys, referenced);

        var files = 0;
        long bytes = 0;

        foreach (var id in orphans)
        {
            var entry = byId[id];
            try
            {
                await _opfs.DeleteAsync(id, entry.Ext);
                files++;
                bytes += entry.SizeBytes;
            }
            catch (Exception ex)
            {
                _errorLog.Log("ProjectStore.SweepUnusedMediaAsync", ex);
            }
        }

        return (files, bytes);
    }

    /// <summary>
    /// Every clip id mentioned by a saved project, by what is open now, or by the media bin.
    /// </summary>
    /// <remarks>
    /// The bin matters as much as the timeline: media imported and not yet placed is media somebody
    /// intends to use, and sweeping it would delete a file out from under the panel showing it.
    /// </remarks>
    private async Task<HashSet<Guid>> CollectReferencedClipIdsAsync()
    {
        var referenced = new HashSet<Guid>();

        foreach (var item in _clips.Tracks.SelectMany(t => t.Items)) referenced.Add(item.Id);
        foreach (var item in _clips.MediaBin) referenced.Add(item.Id);

        foreach (var summary in Projects.ToList())
        {
            try
            {
                var json = await (await StorageAsync())
                    .InvokeAsync<string?>("getItem", $"{EntryPrefix}{summary.Id}");
                if (string.IsNullOrEmpty(json)) continue;

                var (file, _) = ProjectSerializer.Parse(json);
                if (file is null) continue;

                foreach (var id in ReferencedIds(file)) referenced.Add(id);
            }
            catch (Exception ex)
            {
                // A project that cannot be read has to be assumed to reference everything, so the
                // sweep is abandoned rather than run on partial information.
                _errorLog.Log("ProjectStore.CollectReferencedClipIdsAsync", ex);
                throw;
            }
        }

        return referenced;
    }

    private static IEnumerable<Guid> ReferencedIds(ProjectFile file)
    {
        foreach (var track in file.Tracks)
        {
            foreach (var c in track.VideoClips) yield return c.Id;
            foreach (var c in track.AudioClips) yield return c.Id;
            foreach (var c in track.ImageClips) yield return c.Id;
            foreach (var c in track.CalloutClips) yield return c.Id;
        }

        foreach (var c in file.Bin.VideoClips) yield return c.Id;
        foreach (var c in file.Bin.AudioClips) yield return c.Id;
        foreach (var c in file.Bin.ImageClips) yield return c.Id;
    }

    // ── Autosave ──────────────────────────────────────────────────────────────

    /// <summary>How long the editing has to stop before the project is written.</summary>
    /// <remarks>
    /// Long enough that a drag or a burst of typing writes once at the end rather than continually,
    /// short enough that walking away from the machine has already saved.
    /// </remarks>
    public static readonly TimeSpan AutosaveIdle = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _autosaveCts;
    private bool _autosaveEnabled;
    private bool _restoring;
    private bool _indexWasRead;

    /// <summary>True while an autosave is scheduled and has not run.</summary>
    /// <remarks>Read by the unload guard: work that has not reached storage is worth stopping for.</remarks>
    public bool AutosavePending => _autosaveCts is { IsCancellationRequested: false };

    /// <summary>When the project was last written, for the "Saved ·" hint.</summary>
    public DateTime? LastSavedAt { get; private set; }

    /// <summary>
    /// Starts writing the project a couple of seconds after editing stops.
    /// </summary>
    /// <remarks>
    /// <para>Nothing wrote anything unless somebody chose Save. A reload, a crashed tab or a closed
    /// window took the whole session with it, and the editor is exactly the kind of place people
    /// work for an hour without thinking about files (2026-09-05 audit, F9).</para>
    ///
    /// <para>A project that has never been saved gets a name of its own rather than being skipped,
    /// because an unnamed project is precisely the one most likely to be lost.</para>
    /// </remarks>
    public void EnableAutosave() => _autosaveEnabled = true;

    private void ScheduleAutosave()
    {
        if (!_autosaveEnabled) return;

        _autosaveCts?.Cancel();
        _autosaveCts?.Dispose();
        _autosaveCts = new CancellationTokenSource();

        _ = AutosaveAfterIdleAsync(_autosaveCts.Token);
    }

    private async Task AutosaveAfterIdleAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(AutosaveIdle, token);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by a later edit
        }

        await FlushAutosaveAsync();
    }

    /// <summary>
    /// Writes the project now, if there is anything to write.
    /// </summary>
    /// <remarks>
    /// Also called as the page is hidden, which is the last chance a tab gets and the one path that
    /// fires on every way out, including switching apps on a phone.
    /// </remarks>
    public async Task FlushAutosaveAsync()
    {
        if (!_autosaveEnabled || !IsDirty) return;

        try
        {
            await SaveAsync(CurrentProjectName);
        }
        catch (ProjectStorageException)
        {
            // Storage refused. SaveAsync has already left the project dirty and logged it; an
            // autosave is not the place to interrupt somebody, and the unload guard will still
            // stop them leaving with the work unsaved.
        }
        catch (Exception ex)
        {
            _errorLog.Log("ProjectStore.FlushAutosaveAsync", ex);
        }
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

            // Whether the list was actually read, as distinct from what it contained. Housekeeping
            // refuses to run without this, because a failure to read makes every stored file look
            // unreferenced — and sweeping on that basis deletes the media for every project the
            // person has (2026-09-05 audit, media-2).
            _indexWasRead = true;
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

        _restoring = true;
        try { _projectService.RestoreAsync(file); }
        finally { _restoring = false; }

        CurrentProjectName = name;

        // The server's row, not this browser's key. Setting the local key from the server id is
        // what used to fork a server project into browser storage under a foreign identifier
        // (2026-09-05 audit, F15).
        CurrentServerId    = serverId;
        CurrentLocalId     = null;
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

        var id = CurrentLocalId ?? Guid.NewGuid();
        CurrentLocalId = id;

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
            IsDirty     = false;
            LastSavedAt = DateTime.Now;

            // A save is a save, however it was triggered — nothing is pending any more.
            _autosaveCts?.Cancel();
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

            var (file, problem) = ProjectSerializer.Parse(json);
            if (file is null)
            {
                _errorLog.Log("ProjectStore.OpenAsync", problem ?? "Stored project could not be read.");
                return;
            }

            _restoring = true;
            try { _projectService.RestoreAsync(file); }
            finally { _restoring = false; }

            var summary = Projects.FirstOrDefault(p => p.Id == id);
            CurrentProjectName = summary?.Name ?? file.ProjectName ?? DefaultName();
            CurrentLocalId     = id;
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
        if (CurrentLocalId == id) CurrentProjectName = trimmed;

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
            if (CurrentLocalId == id) await PersistActiveIdAsync(null);
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
        CurrentLocalId     = null;
        CurrentServerId    = null;
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

        // Nothing to restore, so nothing to start an engine for.
        if (!file.Tracks.Any(t => t.VideoClips.Count > 0 || t.AudioClips.Count > 0
                               || t.ImageClips.Count > 0)
            && file.Bin.IsEmpty)
            return;

        // The engine has to be running before anything can be written into its filesystem, and on
        // a reload it is not: the project came back with every clip marked missing and the media
        // sitting right there in storage, waiting for somebody to press Initialize — which nothing
        // asked them to do (2026-09-05 audit, persistence-16). A project with clips in it is
        // reason enough to start the engine.
        if (_ffmpeg.State is FfmpegState.Idle)
        {
            try { await _ffmpeg.LoadAsync(); }
            catch (Exception ex) { _errorLog.Log("ProjectStore.RestoreOpfsFilesAsync", ex); }
        }

        // Still waited for, because loading takes a moment and another caller may already have
        // started it. The bound is what stops a wedged engine holding this forever.
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
                    var memFsName = await RestoreOneAsync(vc.Id, vc.SourceBinId, vc.OpfsExt!);
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
                    var memFsName = await RestoreOneAsync(ac.Id, ac.SourceBinId, ac.OpfsExt!);
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
                    var memFsName = await RestoreOneAsync(ic.Id, ic.SourceBinId, ic.OpfsExt!);
                    if (memFsName is null) continue;
                    clip.MemFsName      = memFsName;
                    clip.IsMediaMissing = false;
                }
                catch (Exception ex) { _errorLog.Log("ProjectStore.RestoreOpfs(image)", ex); }
            }
        }

        // The media bin too. Its entries are what a placed clip's media is actually filed under,
        // and they are also placeable in their own right — a bin left unmounted shows cards that
        // cannot be added to the timeline.
        foreach (var (id, ext) in file.Bin.VideoClips.Where(c => c.OpfsExt is not null)
                                          .Select(c => (c.Id, c.OpfsExt!))
                     .Concat(file.Bin.AudioClips.Where(c => c.OpfsExt is not null)
                                          .Select(c => (c.Id, c.OpfsExt!)))
                     .Concat(file.Bin.ImageClips.Where(c => c.OpfsExt is not null)
                                          .Select(c => (c.Id, c.OpfsExt!))))
        {
            try
            {
                var entry = _clips.MediaBin.FirstOrDefault(i => i.Id == id);
                if (entry is null) continue;

                var memFsName = await RestoreFromAsync(id, ext);
                if (memFsName is null) continue;

                switch (entry)
                {
                    case VideoClip v: v.MemFsName = memFsName; break;
                    case AudioClip a: a.MemFsName = memFsName; break;
                    case ImageClip i: i.MemFsName = memFsName; break;
                }

                entry.IsMediaMissing = false;
            }
            catch (Exception ex) { _errorLog.Log("ProjectStore.RestoreOpfs(bin)", ex); }
        }

        // Pre-existing gap, surfaced by item #69's fix: nothing above notifies the ClipStore
        // after flipping each clip's IsMediaMissing to false, so the timeline's "media missing"
        // warning chip stayed stale until some unrelated re-render happened to clear it. Only
        // reachable via a manual File > Open before now — the restore-on-startup path this phase
        // adds makes it visible on every single restore, so a one-time notify once the whole
        // batch finishes is worth doing here rather than leaving a fixed project looking broken.
        _clips.NotifyChanged();

        // Whatever is still missing may be on the server.
        await OfferToRefetchAsync();
    }

    // ── Re-fetching missing media ─────────────────────────────────────────────

    /// <summary>
    /// The clips a re-fetch could bring back, when there are any.
    /// </summary>
    /// <remarks>
    /// Set when a restore left media missing that the server can supply and the total is large
    /// enough to be worth asking about. The host renders the question; this store never decides on
    /// its own to move hundreds of megabytes.
    /// </remarks>
    public IReadOnlyList<MediaRelinkCandidate> PendingRefetch { get; private set; } = [];

    /// <summary>Raised when <see cref="PendingRefetch"/> becomes non-empty.</summary>
    public event Action? OnRefetchOffered;

    /// <summary>
    /// The placed clips a re-fetch applies to.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately not the media bin. A project saved without a bin section is given one on
    /// open, seeded from the timeline with a <b>fresh id per entry</b> — so fetching media for a
    /// bin entry writes a new copy of the file under a new id on every single reload. Found on
    /// screen: one reload of a one-clip project left three copies of the same file in storage.</para>
    ///
    /// <para>Bin entries do not need their own copy. They are clones of a placed clip, made to
    /// represent the source in the panel, and <see cref="ShareMediaWithBinEntriesAsync"/> points
    /// each one at the media its clip already has.</para>
    /// </remarks>
    private IEnumerable<TrackItem> AllRestorableItems =>
        _clips.AllVideoClips.Cast<TrackItem>()
            .Concat(_clips.AllAudioClips)
            .Concat(_clips.AllImageClips);

    /// <summary>
    /// Points each media-bin entry at the media the clip it was cloned from now has.
    /// </summary>
    /// <remarks>
    /// The bin card is a view of a source, not a second copy of it. Sharing the placed clip's
    /// mounted file is what the bin does within a session anyway.
    /// </remarks>
    private void ShareMediaWithBinEntries()
    {
        foreach (var entry in _clips.MediaBin.Where(e => e.IsMediaMissing))
        {
            var placed = _clips.AllVideoClips.Cast<TrackItem>()
                .Concat(_clips.AllAudioClips)
                .Concat(_clips.AllImageClips)
                .FirstOrDefault(c => c.SourceBinId == entry.Id && !c.IsMediaMissing);

            if (placed is null) continue;

            var memFsName = placed switch
            {
                VideoClip v => v.MemFsName,
                AudioClip a => a.MemFsName,
                ImageClip i => i.MemFsName,
                _           => null,
            };
            if (memFsName is null) continue;

            switch (entry)
            {
                case VideoClip v: v.MemFsName = memFsName; break;
                case AudioClip a: a.MemFsName = memFsName; break;
                case ImageClip i: i.MemFsName = memFsName; break;
            }

            entry.IsMediaMissing = false;
        }
    }

    /// <summary>
    /// Fetches missing media back from the server, or asks first when there is a lot of it.
    /// </summary>
    /// <remarks>
    /// <para>This is what makes a project portable. Restoring reads this browser's storage by clip
    /// id, so a project opened on a second machine — or after the storage was cleared — came back
    /// with every clip missing and a manual re-link per clip as the only way out, while help
    /// promised you could pick a project up on another machine (2026-09-05 audit, F14).</para>
    ///
    /// <para>Small fetches happen without asking, because being asked about four megabytes is
    /// noise. Anything larger, or anything whose size was never recorded, waits for an answer —
    /// somebody opening a project on a tethered phone to check one title should not have an
    /// evening's session recordings start moving in silence.</para>
    /// </remarks>
    private async Task OfferToRefetchAsync()
    {
        if (_relink is null) return;

        // Clip art first, and never asked about: an icon is kilobytes, and the alternative is a
        // layer that vanishes from the finished video while the timeline looks fine
        // (2026-09-05 audit, callouts-14).
        var art = _clips.AllClipArtClips.ToList();
        if (art.Count > 0)
        {
            var restoredArt = await _relink.RestoreClipArtAsync(art);
            if (restoredArt > 0 || art.Any(a => a.IsMediaMissing)) _clips.NotifyChanged();
        }

        if (!_relink.IsAvailable) return;

        var candidates = MediaRelinkService.Candidates(AllRestorableItems);
        if (candidates.Count == 0) return;

        if (MediaRelinkPlan.ShouldAskFirst(candidates))
        {
            PendingRefetch = candidates;
            OnRefetchOffered?.Invoke();
            return;
        }

        await RefetchMissingMediaAsync(candidates);
    }

    /// <summary>
    /// Fetches the media for <paramref name="candidates"/>, or for everything offered.
    /// </summary>
    /// <remarks>
    /// Public so a host can call it when somebody answers the question. Nothing here can make a
    /// project worse: a clip whose file could not be fetched, or came back different, is left
    /// exactly as it was.
    /// </remarks>
    public async Task<MediaRelinkService.Outcome> RefetchMissingMediaAsync(
        IReadOnlyList<MediaRelinkCandidate>? candidates = null, CancellationToken ct = default)
    {
        PendingRefetch = [];
        if (_relink is null) return new MediaRelinkService.Outcome(0, 0, 0);

        var wanted = (candidates ?? MediaRelinkService.Candidates(AllRestorableItems))
            .Select(c => c.ClipId)
            .ToHashSet();

        var items = AllRestorableItems.Where(i => wanted.Contains(i.Id)).ToList();
        if (items.Count == 0) return new MediaRelinkService.Outcome(0, 0, 0);

        var outcome = await _relink.RelinkAsync(items, ct);

        if (outcome.Restored > 0)
        {
            ShareMediaWithBinEntries();
            _clips.NotifyChanged();
        }
        return outcome;
    }

    /// <summary>Dismisses the offer without fetching anything.</summary>
    public void DeclineRefetch()
    {
        PendingRefetch = [];
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Item #38 phase B — restores one clip's OPFS copy as a zero-copy WORKERFS mount instead of a
    /// full MEMFS byte copy (the pre-phase-B behavior). Falls back to a real copy when mounting
    /// fails for any reason (e.g. this browser's OPFS doesn't support the underlying mount
    /// mechanics), so a project always finishes restoring even on a browser where mounting isn't
    /// available — just without the memory win.
    /// </summary>
    /// <param name="sourceBinId">
    /// The media-bin entry this clip was placed from, when it was.
    /// </param>
    /// <remarks>
    /// <para>The stored file is named after whichever clip first imported it, and placing from the
    /// bin makes a copy with an id of its own. So a placed clip's media is filed under its bin
    /// entry's id, not its own — and looking only under its own id found nothing, which meant that
    /// since the media bin was introduced, no placed clip's media had ever come back after a
    /// reload. The project restored, the file was sitting right there, and every clip said
    /// "missing" (found on screen while verifying phase 5 of the 2026-09-05 audit).</para>
    ///
    /// <para>Its own id is still tried first, because a clip imported straight onto the timeline
    /// before the bin existed is filed under that.</para>
    /// </remarks>
    private async Task<string?> RestoreOneAsync(Guid clipId, Guid? sourceBinId, string opfsExt)
    {
        // The same two places the live player looks, and for the same reason — see MediaStorage.
        foreach (var id in MediaStorage.CandidateIds(clipId, sourceBinId))
        {
            var restored = await RestoreFromAsync(id, opfsExt);
            if (restored is not null) return restored;
        }

        return null;
    }

    private async Task<string?> RestoreFromAsync(Guid storedId, string opfsExt)
    {
        var mounted = await _mounter.MountAsync(storedId, opfsExt);
        if (mounted is not null) return mounted;

        var jsFile = await _opfs.ReadAsJSFileAsync(storedId, opfsExt);
        if (jsFile is null) return null;
        var memFsName = $"{storedId}{opfsExt}";
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
