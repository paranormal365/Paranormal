using Ben.Video.Editor.Models;
using Microsoft.JSInterop;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Singleton service that owns the ffmpeg.wasm JS object reference and manages
/// the FFmpeg state machine: Idle → LoadingCore → Ready → Processing → Ready/Error.
/// </summary>
public sealed class FfmpegService : IAsyncDisposable
{
    private const string ModulePath = "/_content/Ben.Video.Editor/js/ffmpegInterop.js";

    private readonly IJSRuntime _js;
    private readonly ErrorLogService _errorLog;
    private readonly MemFsLedger _memFsLedger;
    private IJSObjectReference? _module;
    private DotNetObjectReference<FfmpegService>? _selfRef;

    public FfmpegState State { get; private set; } = FfmpegState.Idle;
    public string? LastError { get; private set; }
    public int ProgressPercent { get; private set; }
    public string? DownloadLabel { get; private set; }

    // Recent ffmpeg log lines, newest last — kept so a failed command can report WHY it
    // failed instead of just a bare exit code. Small fixed cap; ffmpeg is chatty.
    // NOTE: do NOT pattern-match log lines for crash detection — the single-threaded
    // ffmpeg.wasm core prints a benign "Aborted()" line as part of every command's normal
    // exit path (verified live: imports/exports succeed right through them). The exit code
    // each command returns is the only reliable failure signal.
    private const int LogTailCapacity = 40;
    private readonly Queue<string> _logTail = new(LogTailCapacity);

    /// <summary>The most recent ffmpeg log lines (up to 40), oldest first.</summary>
    public IReadOnlyCollection<string> LogTail => _logTail;

    // Item #59-#65 flakiness investigation, phase 141 — a structured trace of every worker-
    // touching operation (name, when it started, how long it took, whether it succeeded), kept
    // separately from _logTail (ffmpeg's own stdout, opaque to .NET) and from ErrorLogService
    // (user-facing, exportable, error-only per its own doc comment). This is the "did anything
    // actually happen" instrument phase 142 uses to prove commands stop overlapping, and the
    // basis for phase 143's watchdog (a command with a start but no matching finish, past its
    // policy's timeout, with no fresh log line either, is the wedge signal).
    private const int OperationTraceCapacity = 200;
    private readonly Queue<FfmpegOperationTrace> _operationTrace = new(OperationTraceCapacity);

    /// <summary>The most recent worker operations (up to 200), oldest first.</summary>
    public IReadOnlyCollection<FfmpegOperationTrace> OperationTrace => _operationTrace;

    // Phase 142 — the worker's own EXEC handler is fully synchronous with an infinite timeout, so
    // every queued WRITE_FILE/READ_FILE/FFPROBE/MOUNT message behind a running command blocks
    // until it finishes. Before this phase nothing enforced that on the C# side: import
    // (GetMetadataAsync/ExtractThumbnailsAsync/WriteFile*) never set Processing at all, so it
    // freely interleaved with auto-preview concats, background-render transfers, and Export on the
    // same instance — live-verified in phase 141's diagnostics panel (a 22s concatClips and a
    // 10.7s exec both ran *during* a single-clip import). This lock makes that structurally
    // impossible: every worker-touching public method now acquires it before doing anything, and
    // queued callers simply wait their turn instead of racing. Not reentrant — see the
    // *CoreAsync methods (currently just ExecCoreAsync) for the handful of cases where one public
    // method's own logic needs to invoke another's underlying command while already holding it.
    private readonly SemaphoreSlim _workerLock = new(1, 1);
    private int _busyDepth;

    /// <summary>
    /// True while any worker-touching call is in flight — acquired the moment a caller starts
    /// waiting on <see cref="_workerLock"/>, not just once it's actually running. Distinct from
    /// <see cref="State"/>: State only flips to <see cref="FfmpegState.Processing"/> for the "big"
    /// commands (exec/concat/trim/etc, unchanged from before this phase) — this is accurate for
    /// every call, including the import-path ones that never touched State at all. Lets a caller
    /// (e.g. an import queued behind an in-flight auto-preview render) show "waiting for ffmpeg…"
    /// instead of looking indistinguishable from a wedge.
    /// </summary>
    public bool IsWorkerBusy => Volatile.Read(ref _busyDepth) > 0;

    // Phase 143 — a genuinely wedged command (no library-level timeout applies to it, or the
    // library's own timeout mechanism somehow still didn't save it) has no other way to surface
    // itself: the awaited JS promise just never settles. A periodic timer is the only way to
    // notice that from the outside; see WorkerWatchdog's own doc comment for why 45s of silence
    // (not "any command running past 45s") is the actual signal.
    private readonly WorkerWatchdog _watchdog;
    private readonly Timer _watchdogTimer;

    /// <summary>True once the watchdog has flagged the current in-flight command as wedged.
    /// Never auto-clears itself — only <see cref="ResetWorkerAsync"/> (user-initiated, via a
    /// Reset control) or the command finishing/failing on its own clears it.</summary>
    public bool IsWorkerWedged => _watchdog.IsWedged;

    /// <summary>Fires once when the watchdog first flags a wedge — the UI's cue to offer Reset.</summary>
    public event Action? OnWorkerWedged;

    public event Action? OnStateChanged;

    public FfmpegService(IJSRuntime js, ErrorLogService errorLog, MemFsLedger memFsLedger, WorkerWatchdog watchdog,
        TimeSpan? watchdogPollInterval = null)
    {
        _js = js;
        _errorLog = errorLog;
        _memFsLedger = memFsLedger;
        _watchdog = watchdog;
        _watchdog.OnWedged += HandleWedged;
        // Test hook: production always uses the 5s default; tests pass a short interval so a
        // wedge test doesn't need to wait 45+ real seconds for a poll tick to land.
        var interval = watchdogPollInterval ?? TimeSpan.FromSeconds(5);
        _watchdogTimer = new Timer(_ => _watchdog.Evaluate(), null, interval, interval);
    }

    private void HandleWedged()
    {
        _errorLog.Log("FfmpegService.Watchdog",
            $"ffmpeg worker appears wedged — no activity for {WorkerWatchdog.DefaultWedgeThreshold.TotalSeconds:0}s while a command was in flight.");
        OnWorkerWedged?.Invoke();
        OnStateChanged?.Invoke(); // lets the UI re-render its Reset affordance
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Lazy-loads the JS module and initializes the ffmpeg.wasm core.
    /// Safe to call multiple times — no-op if already Ready.
    /// </summary>
    public async Task LoadAsync()
    {
        if (State is FfmpegState.Ready or FfmpegState.LoadingCore) return;

        await WithLockAsync(requireReady: false, async () =>
        {
            // Re-check after acquiring: another caller may have already loaded (or be loading)
            // the core while this one was queued behind some other in-flight command.
            if (State is FfmpegState.Ready or FfmpegState.LoadingCore) return;

            try
            {
                SetState(FfmpegState.LoadingCore);
                _logTail.Clear();

                _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
                _selfRef ??= DotNetObjectReference.Create(this);

                var multiThread = await _module.InvokeAsync<bool>("isMultiThreadSupported");
                await InvokeTracedAsync("loadCore", () => _module.InvokeVoidAsync("loadCore", _selfRef, multiThread).AsTask());

                SetState(FfmpegState.Ready);
            }
            catch (Exception ex)
            {
                RecordFailure(nameof(LoadAsync), ex);
                throw;
            }
        });
    }

    /// <summary>Terminates the ffmpeg worker and resets to Idle. Deliberately does NOT acquire
    /// <see cref="_workerLock"/> — until phase 143 adds real timeouts, a wedged in-flight command
    /// would hold the lock forever, and Terminate must remain able to run even then (it's the
    /// escape hatch a future Reset button depends on).</summary>
    public async Task TerminateAsync()
    {
        if (_module is not null)
        {
            await InvokeTracedAsync("terminate", () => _module.InvokeVoidAsync("terminate").AsTask());
        }
        // A real terminate() wipes the worker's whole virtual filesystem (see terminate()'s own
        // doc comment on TerminateAsync's callers) — every ledger entry is now stale.
        _memFsLedger.Clear();
        SetState(FfmpegState.Idle);
    }

    // ─── JS Callbacks ────────────────────────────────────────────────────────

    [JSInvokable]
    public void OnFfmpegLog(string message)
    {
        if (_logTail.Count >= LogTailCapacity) _logTail.Dequeue();
        _logTail.Enqueue(message);
        _watchdog.RecordActivity(); // a log line is proof of life, even from a "slow" command
    }

    [JSInvokable]
    public void OnFfmpegDownload(string label, int percent)
    {
        // percent == -1 signals indeterminate ("Initializing…")
        DownloadLabel = percent < 0 ? label : $"{label} {percent}%";
        _watchdog.RecordActivity();
        OnStateChanged?.Invoke();
    }

    [JSInvokable]
    public void OnFfmpegProgress(int percent, double time)
    {
        ProgressPercent = percent;
        _watchdog.RecordActivity();
        OnStateChanged?.Invoke();
    }

    // ─── Operations ──────────────────────────────────────────────────────────

    /// <summary>Write a browser File object into ffmpeg MEMFS.</summary>
    public Task WriteFileAsync(string name, IJSObjectReference fileRef) => WithLockAsync(requireReady: true, async () =>
    {
        await InvokeTracedAsync("writeFile", () => _module!.InvokeVoidAsync("writeFile", name, fileRef).AsTask());
        // Size unknown — fileRef is an opaque JS File/Blob reference and this call site never
        // reads its .size. See MemFsLedger.Track's own doc comment for why this entry still
        // exists (just doesn't contribute to TotalBytes).
        _memFsLedger.Track(name, 0, "source-fallback");
    });

    /// <summary>
    /// Write raw bytes (e.g. downloaded from an HTTP API) into ffmpeg MEMFS.
    /// The bytes are passed as a <see cref="byte[]"/>; the JS side receives them
    /// as a <c>Uint8Array</c> and writes directly via <c>ffmpeg.writeFile()</c>.
    /// </summary>
    public Task WriteFileFromBytesAsync(string name, byte[] bytes) => WithLockAsync(requireReady: true, async () =>
    {
        await InvokeTracedAsync("writeFileFromBytes", () => _module!.InvokeVoidAsync("writeFileFromBytes", name, bytes).AsTask());
        _memFsLedger.Track(name, bytes.LongLength, "write");
    });

    /// <summary>
    /// Writes bytes into MEMFS once this (main) instance reaches <see cref="FfmpegState.Ready"/>,
    /// retrying every 250ms if it's mid-Preview/mid-Export in the meantime — item #36 phase C.
    /// Extracted from <c>RenderWorkerBackend.TransferToMainAsync</c> (item #38 phase 123) so
    /// <c>NativeSidecarBackend</c> can land its own finished segments through the identical
    /// wait-don't-fail contract instead of duplicating the retry loop: waiting here is correct
    /// because this always runs on a background render loop with nothing better to do, and
    /// failing instead would back off a perfectly good render's signature forever.
    /// </summary>
    public async Task WriteFileWhenReadyAsync(string name, byte[] bytes, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (State == FfmpegState.Ready)
            {
                try
                {
                    await WriteFileFromBytesAsync(name, bytes);
                    return;
                }
                catch (InvalidOperationException) { /* lost the race to a Preview/Export start — retry */ }
            }
            await Task.Delay(250, ct);
        }
    }

    /// <summary>Read a MEMFS file back as a byte array.</summary>
    public Task<byte[]> ReadFileAsync(string name) => WithLockAsync(requireReady: true,
        () => InvokeTracedAsync("readFile", () => _module!.InvokeAsync<byte[]>("readFile", name).AsTask()));

    /// <summary>
    /// Reads a MEMFS file back once this (main) instance reaches <see cref="FfmpegState.Ready"/>,
    /// retrying every 250ms if it's mid-Preview/mid-Export in the meantime — same contention this
    /// (main) instance can hit on the write side (see <see cref="WriteFileWhenReadyAsync"/>), just
    /// reached from <c>RenderWorkerBackend.ResolveSourceAsync</c>'s non-OPFS MEMFS-copy fallback
    /// instead. Waiting here is correct for the same reason: this always runs on a background
    /// render loop with nothing better to do, and failing instead would surface the raw "not
    /// ready" exception to the user and needlessly back off a perfectly good render.
    /// </summary>
    public async Task<byte[]> ReadFileWhenReadyAsync(string name, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (State == FfmpegState.Ready)
            {
                try
                {
                    return await ReadFileAsync(name);
                }
                catch (InvalidOperationException) { /* lost the race to a Preview/Export start — retry */ }
            }
            await Task.Delay(250, ct);
        }
    }

    /// <summary>Delete a MEMFS file to reclaim worker RAM.</summary>
    public Task DeleteFileAsync(string name) => WithLockAsync(requireReady: false, async () =>
    {
        if (_module is null) return;
        await InvokeTracedAsync("deleteFile", () => _module.InvokeVoidAsync("deleteFile", name).AsTask());
        _memFsLedger.Untrack(name);
    });

    /// <summary>
    /// Item #38 phase B — zero-copy-mounts a browser <see cref="IJSObjectReference"/> (a <c>File</c>,
    /// e.g. from <see cref="OPFSService.ReadAsJSFileAsync"/>) into this (the main) ffmpeg instance via
    /// WORKERFS and returns the resulting path, ready to use as an <c>-i</c> input. WORKERFS mounts
    /// are read-only and cost no WASM heap — this is the same mechanism
    /// <see cref="RenderWorkerService.MountSourceAsync"/> already uses on the background render
    /// worker, now available on the main instance too (<c>ffmpegInterop.js</c>'s <c>mountWorkerFs</c>
    /// existed for this from the start but had no caller here). Returns <c>null</c> on any failure —
    /// callers fall back to <see cref="WriteFileAsync"/>/<see cref="WriteFileFromBytesAsync"/> (a real
    /// MEMFS copy) in that case, exactly as every import path already did before this phase.
    /// </summary>
    public Task<string?> MountWorkerFsAsync(IJSObjectReference fileRef, string mountDir) => WithLockAsync<string?>(requireReady: false, async () =>
    {
        if (_module is null) return null;
        try
        {
            return await InvokeTracedAsync("mountWorkerFs", () => _module.InvokeAsync<string?>("mountWorkerFs", fileRef, mountDir).AsTask());
        }
        catch (Exception ex)
        {
            // Item #59-#65 flakiness investigation — this fallback was previously silent
            // (catch { return null; }); a mount failure now at least explains itself before
            // the caller falls back to a real MEMFS copy. Not an Error-state transition: this
            // is a normal, expected fallback path (e.g. WORKERFS unsupported), not a wedge.
            _errorLog.Log("FfmpegService.MountWorkerFsAsync", $"mount failed for {mountDir}: {ex.Message}", ex.ToString());
            return null;
        }
    });

    /// <summary>Unmounts a directory previously mounted by <see cref="MountWorkerFsAsync"/>. Never
    /// use <see cref="DeleteFileAsync"/> on a mounted path — mounts are unmount-only, not delete-able
    /// MEMFS files, and this is the seam that keeps that contract from being violated by accident.</summary>
    public Task UnmountWorkerFsAsync(string mountDir) => WithLockAsync(requireReady: false, async () =>
    {
        if (_module is null) return;
        try
        {
            await InvokeTracedAsync("unmountWorkerFs", () => _module.InvokeVoidAsync("unmountWorkerFs", mountDir).AsTask());
        }
        catch (Exception ex)
        {
            _errorLog.Log("FfmpegService.UnmountWorkerFsAsync", $"unmount failed for {mountDir}: {ex.Message}", ex.ToString());
        }
    });

    /// <summary>
    /// Execute an FFmpeg command. Queuing is the caller's responsibility.
    /// Throws <see cref="InvalidOperationException"/> (with the recent ffmpeg log tail in the
    /// message) when the command exits non-zero — previously the exit code was returned and
    /// every caller discarded it, letting a failed pass silently feed its missing/broken
    /// output into the next one (backlog #29's "audio-only export" symptom).
    /// </summary>
    /// <param name="ct">Audit #1 — observed while <b>queueing</b> for the worker lock and again
    /// immediately before dispatch, so a cancelled export stops at the next command boundary
    /// instead of running every remaining command to completion. It cannot abort a command that is
    /// already executing: ffmpeg.wasm's worker is synchronous and exposes no abort channel, and the
    /// only lever that stops one mid-flight is <c>terminate()</c>, which destroys the worker and
    /// every cached MEMFS segment — deliberately not done here (phase 143's standing rule: never
    /// kill an in-flight export without consent).</param>
    public Task<int> ExecAsync(string[] args, CancellationToken ct = default) =>
        WithLockAsync(requireReady: true, () =>
        {
            // Between acquiring the lock and dispatching, an arbitrary amount of time may have
            // passed waiting behind another command — re-check rather than firing a command the
            // caller has since abandoned.
            ct.ThrowIfCancellationRequested();
            return ExecCoreAsync(args);
        }, ct);

    /// <summary>
    /// The actual "exec" call, without acquiring <see cref="_workerLock"/> or checking
    /// <see cref="EnsureReady"/> — the caller must already hold the lock and have confirmed
    /// Ready. Exists so <see cref="ExtractAudioAsync"/> can run a command while it's already
    /// holding the (non-reentrant) lock, instead of calling the public <see cref="ExecAsync"/>
    /// and deadlocking against itself.
    /// </summary>
    private async Task<int> ExecCoreAsync(string[] args)
    {
        SetState(FfmpegState.Processing);
        int code;
        try
        {
            code = await InvokeTracedAsync("exec", () => _module!.InvokeAsync<int>("exec", new object[] { args, FfmpegTimeoutPolicy.GenericExecMs }).AsTask());
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(ExecAsync), ex);
            throw;
        }

        ThrowIfFailed(code, "ffmpeg");
        SetState(FfmpegState.Ready);
        return code;
    }

    /// <summary>
    /// Shared post-command check: throws (with the recent ffmpeg log tail) when the command
    /// exited non-zero. The exit code is the only reliable failure signal — see the note on
    /// <see cref="_logTail"/> about why log lines must not be pattern-matched for crashes.
    /// State returns to Ready (not Error): the core itself keeps working after a failed
    /// command, so the caller can fix the input and retry without re-initializing.
    /// </summary>
    private void ThrowIfFailed(int code, string what)
    {
        if (code == 0) return;

        LastError = $"{what} exited with code {code}.";
        var message = $"{what} exited with code {code}. Recent log:\n{BuildLogTailText()}";
        _errorLog.Log($"FfmpegService.{what}", LastError, BuildLogTailText());
        SetState(FfmpegState.Ready);
        throw new InvalidOperationException(message);
    }

    private string BuildLogTailText() =>
        _logTail.Count == 0 ? "(no log captured)" : string.Join("\n", _logTail.TakeLast(12));

    /// <summary>Extract metadata (duration, width, height) from a MEMFS video file.</summary>
    public Task<VideoMetadata> GetMetadataAsync(string inputName) => WithLockAsync(requireReady: true, async () =>
    {
        try
        {
            return await InvokeTracedAsync("getMetadata", () => _module!.InvokeAsync<VideoMetadata>("getMetadata", inputName, FfmpegTimeoutPolicy.ProbeMs).AsTask());
        }
        catch (Exception ex)
        {
            // Note: unlike ExecAsync, this doesn't transition to Error — matches this phase's
            // "zero happy-path/failure-path behavior change" contract (this call, like
            // ExtractThumbnailsAsync below, never raised Processing/Error in the first place;
            // that's unrelated to the locking added in phase 142). Still worth logging.
            _errorLog.Log("FfmpegService.GetMetadataAsync", ex);
            throw;
        }
    });

    /// <summary>Extract thumbnail WebP blob URLs from a MEMFS video file.</summary>
    public Task<string[]> ExtractThumbnailsAsync(string inputName, int count, double duration) => WithLockAsync(requireReady: true, async () =>
    {
        try
        {
            return await InvokeTracedAsync("extractThumbnails", () => _module!.InvokeAsync<string[]>("extractThumbnails", inputName, count, duration, FfmpegTimeoutPolicy.ThumbnailBatchMs(count)).AsTask());
        }
        catch (Exception ex)
        {
            _errorLog.Log("FfmpegService.ExtractThumbnailsAsync", ex);
            throw;
        }
    });

    /// <summary>Trim a clip with frame-accurate re-encode (libx264).</summary>
    public Task TrimClipAsync(string inputName, string outputName, double startSec, double endSec) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        try
        {
            await InvokeTracedAsync("trimClip", () => _module!.InvokeVoidAsync("trimClip", inputName, outputName, startSec, endSec, FfmpegTimeoutPolicy.TrimMs(startSec, endSec)).AsTask());
            SetState(FfmpegState.Ready);
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(TrimClipAsync), ex);
            throw;
        }
    });

    /// <summary>
    /// Concatenate ordered MEMFS segments into a single output file. When <paramref name="scaleTo"/>
    /// is supplied, the concatenated output is scaled/padded down to that size — used by the editor's
    /// own Preview render to trade quality for speed; never passed by the real export pipeline, which
    /// always renders at full quality regardless of this parameter.
    /// </summary>
    public Task ConcatClipsAsync(string[] segmentNames, string outputName, (int Width, int Height)? scaleTo = null) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        int code;
        try
        {
            // Phase 142: a per-invocation list name, not the old fixed "_concat_list.txt" — the
            // worker lock already makes concurrent concats structurally impossible, but this
            // removes the *need* for that guarantee to hold perfectly, and ffmpegInterop.js now
            // deletes it in a finally so a failed concat doesn't leak it either.
            var listName = $"_concat_{Guid.NewGuid():N}.txt";
            code = await InvokeTracedAsync("concatClips", () => _module!.InvokeAsync<int>("concatClips", new object?[]
            {
                segmentNames, outputName, scaleTo?.Width, scaleTo?.Height, listName, FfmpegTimeoutPolicy.ConcatMs
            }).AsTask());
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(ConcatClipsAsync), ex);
            throw;
        }

        ThrowIfFailed(code, "ffmpeg concat");
        SetState(FfmpegState.Ready);
    });

    /// <summary>
    /// Stream-copy concat (<c>-c copy</c>) — near-instant, no re-encode. Only valid when every
    /// segment shares identical codec/dimensions/fps/audio layout, which the background render
    /// worker's pinned encode args guarantee (item #36 phase D). Use <see cref="ConcatClipsAsync"/>
    /// for anything else.
    /// </summary>
    public Task ConcatCopyAsync(string[] segmentNames, string outputName) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        int code;
        try
        {
            var listName = $"_concat_copy_{Guid.NewGuid():N}.txt"; // see ConcatClipsAsync's own note
            code = await InvokeTracedAsync("concatCopy", () => _module!.InvokeAsync<int>("concatCopy", new object[] { segmentNames, outputName, listName, FfmpegTimeoutPolicy.ConcatMs }).AsTask());
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(ConcatCopyAsync), ex);
            throw;
        }

        ThrowIfFailed(code, "ffmpeg concat (stream copy)");
        SetState(FfmpegState.Ready);
    });

    /// <summary>Download a MEMFS file through the browser save dialog.</summary>
    public Task DownloadFileAsync(string name, string downloadAs, string mimeType) => WithLockAsync(requireReady: true,
        () => InvokeTracedAsync("downloadFile", () => _module!.InvokeVoidAsync("downloadFile", name, downloadAs, mimeType).AsTask()));

    /// <summary>Create a blob: URL for a MEMFS file to drive a &lt;video&gt; element.</summary>
    public Task<string> CreatePreviewUrlAsync(string name, string mimeType = "video/mp4") => WithLockAsync(requireReady: true,
        () => InvokeTracedAsync("createPreviewUrl", () => _module!.InvokeAsync<string>("createPreviewUrl", name, mimeType).AsTask()));

    /// <summary>Rename a MEMFS file in place (item #38 phase D) — a genuine filesystem rename via
    /// ffmpeg.wasm's own <c>rename</c>, not the old read-into-.NET/write-under-new-name/delete-old
    /// round trip <see cref="ExportService"/> used to do.</summary>
    public Task RenameFileAsync(string from, string to) => WithLockAsync(requireReady: false, async () =>
    {
        if (_module is null) return;
        await InvokeTracedAsync("rename", () => _module.InvokeVoidAsync("rename", from, to).AsTask());
        _memFsLedger.Rename(from, to);
    });

    /// <summary>Trigger a browser download from an already-created blob: URL (item #38 phase D) —
    /// used for the OPFS-backed export path. Does not revoke the URL.</summary>
    public Task DownloadBlobUrlAsync(string url, string downloadAs) => WithLockAsync(requireReady: false, async () =>
    {
        if (_module is null) return;
        await InvokeTracedAsync("downloadBlobUrl", () => _module.InvokeVoidAsync("downloadBlobUrl", url, downloadAs).AsTask());
    });

    /// <summary>
    /// Item #38 phase D: moves a finished export from MEMFS into the OPFS <c>bv-exports/</c> area
    /// entirely JS-side (<c>readFile</c> → OPFS write → <c>deleteFile</c>, no byte array crosses
    /// into .NET) instead of retaining a full-size MEMFS copy through the whole download/preview
    /// step. Returns the exported file's size in bytes, or <c>-1</c> if OPFS isn't available/usable
    /// (e.g. Safari private browsing) — the JS side only deletes the MEMFS copy after the OPFS
    /// write succeeds, so a -1 return means <paramref name="memFsName"/> is still safely there for
    /// a caller-side fallback to the pre-phase-D direct-MEMFS path.
    /// </summary>
    public Task<long> ExportToOpfsAsync(string memFsName, Guid exportId, string ext) => WithLockAsync(requireReady: false, async () =>
    {
        if (_module is null) return -1;
        try
        {
            var size = await InvokeTracedAsync("exportToOpfs", () => _module.InvokeAsync<long>("exportToOpfs", memFsName, exportId.ToString("N"), ext).AsTask());
            // Per this method's own doc comment, the JS side only deletes memFsName from MEMFS
            // once the OPFS write actually succeeds (size >= 0) — a -1 return means it's still
            // there for the caller's direct-MEMFS fallback, so the ledger entry must survive too.
            if (size >= 0) _memFsLedger.Untrack(memFsName);
            return size;
        }
        catch (Exception ex)
        {
            _errorLog.Log("FfmpegService.ExportToOpfsAsync", $"OPFS export failed for {memFsName}, falling back to direct MEMFS path: {ex.Message}", ex.ToString());
            return -1;
        }
    });

    /// <summary>
    /// Extract only the audio stream from <paramref name="inputName"/> into <paramref name="outputName"/>.
    /// The output file is written to MEMFS and can be read back with <see cref="ReadFileAsync"/>.
    /// Uses <c>-vn -acodec copy</c> for a fast lossless copy when the source already has a compatible audio stream.
    /// </summary>
    public Task ExtractAudioAsync(string inputName, string outputName) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        try
        {
            // ExecCoreAsync, not the public ExecAsync — this method already holds _workerLock,
            // which is non-reentrant; calling the public ExecAsync here would deadlock forever.
            await ExecCoreAsync(["-i", inputName, "-vn", "-acodec", "copy", "-y", outputName]);
            SetState(FfmpegState.Ready);
        }
        catch (Exception ex)
        {
            // Phase 143 fix (nested-state bug, deliberately left broken through phases 141-142):
            // ExecCoreAsync already resolves its own Ready/Error transition correctly — a merely
            // failed command (non-zero exit) ends Ready via ThrowIfFailed; a genuine worker-level
            // exception ends Error via its own RecordFailure. This outer catch used to
            // unconditionally re-poison State to Error regardless of which one happened, forcing
            // a full reload after e.g. a simple "-acodec copy" incompatibility even though the
            // core was left perfectly healthy. Only escalate when ExecCoreAsync's own failure
            // path DIDN'T already leave State resolved.
            if (State == FfmpegState.Ready)
            {
                LastError = ex.Message;
                _errorLog.Log($"FfmpegService.{nameof(ExtractAudioAsync)}", ex);
            }
            else
            {
                RecordFailure(nameof(ExtractAudioAsync), ex);
            }
            throw;
        }
    });

    /// <summary>Revoke a previously created blob: URL.</summary>
    public Task RevokePreviewUrlAsync(string url) => WithLockAsync(requireReady: false, async () =>
    {
        if (_module is null) return;
        await InvokeTracedAsync("revokePreviewUrl", () => _module.InvokeVoidAsync("revokePreviewUrl", url).AsTask());
    });

    /// <summary>
    /// Execute an arbitrary ffmpeg command with a filter_complex graph.
    /// Full args array must be provided by the caller (no leading "ffmpeg").
    /// </summary>
    public Task<int> ExecFilterComplexAsync(string[] args) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        try
        {
            var code = await InvokeTracedAsync("execFilterComplex", () => _module!.InvokeAsync<int>("execFilterComplex", new object[] { args, FfmpegTimeoutPolicy.GenericExecMs }).AsTask());
            SetState(FfmpegState.Ready);
            return code;
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(ExecFilterComplexAsync), ex);
            throw;
        }
    });

    /// <summary>Write raw bytes directly to MEMFS (no browser File object required).</summary>
    public Task WriteBytesAsync(string name, byte[] data) => WithLockAsync(requireReady: true, async () =>
    {
        await InvokeTracedAsync("writeBytes", () => _module!.InvokeVoidAsync("writeBytes", name, data).AsTask());
        _memFsLedger.Track(name, data.LongLength, "write");
    });

    /// <summary>Apply xfade transitions between consecutive MEMFS segments.</summary>
    public Task<int> ApplyXfadeTransitionsAsync(
        string[] inputNames, string outputName,
        object[] transitions, string[] extraArgs) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        try
        {
            var code = await InvokeTracedAsync("applyXfadeTransitions", () => _module!.InvokeAsync<int>(
                "applyXfadeTransitions",
                inputNames, outputName, transitions, extraArgs, FfmpegTimeoutPolicy.GenericExecMs).AsTask());
            SetState(FfmpegState.Ready);
            return code;
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(ApplyXfadeTransitionsAsync), ex);
            throw;
        }
    });

    /// <summary>Apply a chain of drawtext filters to a MEMFS video file.</summary>
    public Task<int> ApplyDrawtextAsync(
        string inputName, string outputName, string[] filterChain, string[] extraArgs) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        try
        {
            var code = await InvokeTracedAsync("applyDrawtext", () => _module!.InvokeAsync<int>(
                "applyDrawtext",
                inputName, outputName, filterChain, extraArgs, FfmpegTimeoutPolicy.GenericExecMs).AsTask());
            SetState(FfmpegState.Ready);
            return code;
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(ApplyDrawtextAsync), ex);
            throw;
        }
    });

    /// <summary>Mix additional audio MEMFS files into a video output using amix.</summary>
    public Task<int> MixAudioAsync(
        string videoInput, string[] audioInputs,
        string outputName, string audioCodec, int audioBitrateK) => WithLockAsync(requireReady: true, async () =>
    {
        SetState(FfmpegState.Processing);
        try
        {
            var code = await InvokeTracedAsync("mixAudio", () => _module!.InvokeAsync<int>(
                "mixAudio",
                videoInput, audioInputs, outputName, audioCodec, audioBitrateK, FfmpegTimeoutPolicy.GenericExecMs).AsTask());
            SetState(FfmpegState.Ready);
            return code;
        }
        catch (Exception ex)
        {
            RecordFailure(nameof(MixAudioAsync), ex);
            throw;
        }
    });

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetState(FfmpegState state)
    {
        // Item #59-#65 flakiness investigation, phase 141 — reset the displayed progress on
        // entering Processing so "Processing… N%" always means "this command's own progress",
        // never a previous command's leftover value (previously ProgressPercent was untouched
        // here, so a command that starts right after one that finished at 100% would briefly —
        // or, if it stalls, indefinitely — show a stale 100%/whatever% instead of 0%).
        if (state == FfmpegState.Processing) ProgressPercent = 0;
        State = state;
        OnStateChanged?.Invoke();
    }

    private void EnsureReady()
    {
        if (State is not FfmpegState.Ready)
            throw new InvalidOperationException($"FfmpegService is not ready (current state: {State}). Call LoadAsync() first.");
    }

    /// <summary>
    /// Acquires <see cref="_workerLock"/>, optionally checks <see cref="EnsureReady"/> only AFTER
    /// acquiring (deliberately — a caller that arrives while another command is in flight queues
    /// and waits its turn instead of failing fast; by the time it's handed the lock, the prior
    /// command has already resolved State back to Ready, Error, or Idle, which is the state that
    /// actually matters), then runs <paramref name="body"/>. <see cref="IsWorkerBusy"/> is true for
    /// the entire wait-plus-run window, not just the run.
    /// </summary>
    private async Task<T> WithLockAsync<T>(bool requireReady, Func<Task<T>> body, CancellationToken ct = default)
    {
        await _workerLock.WaitAsync(ct);
        Interlocked.Increment(ref _busyDepth);
        try
        {
            if (requireReady) EnsureReady();
            _watchdog.CommandStarted();
            return await body();
        }
        finally
        {
            _watchdog.CommandFinished();
            Interlocked.Decrement(ref _busyDepth);
            _workerLock.Release();
        }
    }

    /// <summary>Void-returning counterpart to <see cref="WithLockAsync{T}"/>.</summary>
    private async Task WithLockAsync(bool requireReady, Func<Task> body, CancellationToken ct = default)
    {
        await _workerLock.WaitAsync(ct);
        Interlocked.Increment(ref _busyDepth);
        try
        {
            if (requireReady) EnsureReady();
            _watchdog.CommandStarted();
            await body();
        }
        finally
        {
            _watchdog.CommandFinished();
            Interlocked.Decrement(ref _busyDepth);
            _workerLock.Release();
        }
    }

    /// <summary>Records a worker command's outcome into <see cref="OperationTrace"/>. Called from
    /// <see cref="InvokeTracedAsync{T}"/> so every JS-interop call site gets this for free without
    /// each of the ~15 call sites duplicating the bookkeeping.</summary>
    private void RecordOperation(string operation, DateTime startedAtUtc, bool success)
    {
        if (_operationTrace.Count >= OperationTraceCapacity) _operationTrace.Dequeue();
        _operationTrace.Enqueue(new FfmpegOperationTrace(operation, startedAtUtc, DateTime.UtcNow - startedAtUtc, success));
    }

    /// <summary>
    /// Wraps a single JS-interop call with operation tracing, leaving the caller's own
    /// state-transition/error-handling logic (SetState(Error), LastError, rethrow, ThrowIfFailed)
    /// completely untouched — this rethrows on failure rather than swallowing, so every existing
    /// try/catch around a call site still runs exactly as before. Diagnostics-only; no behavior
    /// change to the happy or failure path.
    /// </summary>
    private async Task<T> InvokeTracedAsync<T>(string operation, Func<Task<T>> invoke)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            var result = await invoke();
            RecordOperation(operation, startedAt, success: true);
            return result;
        }
        catch
        {
            RecordOperation(operation, startedAt, success: false);
            throw;
        }
    }

    /// <summary>Void-returning counterpart to <see cref="InvokeTracedAsync{T}"/>.</summary>
    private async Task InvokeTracedAsync(string operation, Func<Task> invoke)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            await invoke();
            RecordOperation(operation, startedAt, success: true);
        }
        catch
        {
            RecordOperation(operation, startedAt, success: false);
            throw;
        }
    }

    /// <summary>Shared failure-path bookkeeping for the ~9 call sites that previously each
    /// duplicated <c>LastError = ex.Message; SetState(FfmpegState.Error); throw;</c> — same
    /// behavior, plus now surfaced to <see cref="ErrorLogService"/> (previously invisible: it
    /// wasn't injected into this service at all).</summary>
    private void RecordFailure(string operation, Exception ex)
    {
        LastError = ex.Message;
        _errorLog.Log($"FfmpegService.{operation}", ex);
        SetState(FfmpegState.Error);
    }

    public async ValueTask DisposeAsync()
    {
        await _watchdogTimer.DisposeAsync();
        _watchdog.OnWedged -= HandleWedged;
        _selfRef?.Dispose();

        if (_module is not null)
        {
            // Unconditional — including from FfmpegState.Error, per phase 143: a wedged worker
            // still holds real browser resources (the worker thread, its WASM heap) that a
            // component going away must still release, not just a Ready one.
            try { await _module.InvokeVoidAsync("terminate"); } catch { }
            await _module.DisposeAsync();
        }
    }
}

/// <summary>Metadata returned by ffprobe for a video stream.</summary>
public sealed record VideoMetadata(double Duration, int Width, int Height);

/// <summary>One worker command's outcome, as recorded in <see cref="FfmpegService.OperationTrace"/>
/// (item #59-#65 flakiness investigation, phase 141).</summary>
public sealed record FfmpegOperationTrace(string Operation, DateTime StartedAtUtc, TimeSpan Duration, bool Success);

