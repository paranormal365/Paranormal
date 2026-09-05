using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #69 fix — regression coverage for "File &gt; Save reports success but the project does
/// not restore on reload." Before this fix, nothing in the codebase ever called
/// <see cref="ProjectStore.OpenAsync"/> automatically on startup — this was a wiring gap, not a
/// save/load-format bug, so a plain in-instance JSON round-trip test would NOT have caught it
/// (there wasn't one before this file either). These tests specifically simulate a page reload:
/// two independent <see cref="ProjectStore"/> instances (fresh <see cref="ClipStore"/> each,
/// exactly like a fresh DI scope after a WASM reload) sharing the same backing "localStorage".
/// </summary>
public sealed class ProjectStoreRestoreTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Simulates the browser's localStorage via a plain dictionary, backing ProjectStore's
    /// <c>storageInterop.js</c> module calls (audit #4 replaced the old
    /// <c>eval("localStorage.…")</c> script strings with a typed module).
    ///
    /// <para>The refactor made this fake meaningfully simpler and stricter: it used to regex-parse
    /// generated JS source and un-escape the value out of a string literal, so it was really
    /// asserting on script-construction details. Now it receives the key and value as ordinary
    /// arguments, which is the same shape the real module sees — there is no script to get wrong.</para>
    ///
    /// <para>Sharing one instance's <see cref="Storage"/> dictionary across two
    /// <see cref="ProjectStore"/> instances is what simulates "the same browser, after a reload."</para>
    /// </summary>
    private sealed class FakeLocalStorageJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Storage { get; }

        public FakeLocalStorageJsRuntime(Dictionary<string, string>? shared = null)
            => Storage = shared ?? [];

        /// <summary>When true, every write is refused, as it is in a private window.</summary>
        public bool Refuse { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            // ProjectStore imports the module once and caches the handle.
            if (identifier == "benImportEditorModule")
                return ValueTask.FromResult((TValue)(object)new FakeStorageModule(Storage) { Refuse = Refuse });

            throw new NotSupportedException($"Unexpected top-level JS call: {identifier}");
        }
    }

    /// <summary>The imported <c>storageInterop.js</c> handle — mirrors that module's exports.</summary>
    /// <remarks>
    /// The real module reports whether a write succeeded, because the browser refuses one when its
    /// quota is exhausted or storage is blocked. This mirrors that: <c>Refuse</c> lets a test say
    /// no the way a private window does (2026-09-05 audit, persistence-9).
    /// </remarks>
    private sealed class FakeStorageModule(Dictionary<string, string> storage) : IJSObjectReference
    {
        /// <summary>When true, every write is refused, as it is in a private window.</summary>
        public bool Refuse { get; init; }

        /// <summary>
        /// Returns the boolean the real module returns, or nothing when the caller used
        /// <c>InvokeVoidAsync</c> and does not want an answer.
        /// </summary>
        private static ValueTask<TValue> Result<TValue>(bool value) =>
            typeof(TValue) == typeof(bool)
                ? ValueTask.FromResult((TValue)(object)value)
                : default!;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        {
            switch (identifier)
            {
                case "getItem" when args is [string key]:
                {
                    var value = storage.TryGetValue(key, out var v) ? v : null;
                    return ValueTask.FromResult((TValue)(object?)value!);
                }
                case "setItem" when args is [string key, string value]:
                    if (!Refuse) storage[key] = value;
                    return Result<TValue>(!Refuse);
                case "removeItem" when args is [string key]:
                    storage.Remove(key);
                    return Result<TValue>(true);
                default:
                    throw new NotSupportedException(
                        $"Unexpected storageInterop call: {identifier}({string.Join(", ", args ?? [])})");
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A save the browser refused is reported, not reported as a success.
    /// </summary>
    /// <remarks>
    /// The JavaScript side has always said so: <c>setItem</c> returns false when the quota is
    /// exhausted or storage is blocked, which is what happens in a private window. The C# side
    /// called it through <c>InvokeVoidAsync</c> and threw the answer away, so a save that stored
    /// nothing reported "Project saved." and the work was gone at the next reload (2026-09-05
    /// audit, persistence-9).
    /// </remarks>
    [Fact]
    public async Task SaveAsync_WhenStorageRefuses_SaysSoRatherThanReportingSuccess()
    {
        var storage        = new Dictionary<string, string>();
        var (store, clips) = CreateStore(storage, storageRefuses: true);

        clips.AddClip(new VideoClip { Name = "clip", Duration = 5 });

        var ex = await Assert.ThrowsAsync<ProjectStorageException>(
            () => store.SaveAsync("My Project"));

        Assert.Contains("would not store", ex.Message);
        Assert.Empty(storage);
    }

    [Fact]
    public async Task SaveAsync_WhenStorageRefuses_LeavesTheProjectDirty()
    {
        var (store, clips) = CreateStore([], storageRefuses: true);
        clips.AddClip(new VideoClip { Name = "clip", Duration = 5 });

        await Assert.ThrowsAsync<ProjectStorageException>(() => store.SaveAsync("My Project"));

        // Still unsaved work, so the unload guard and the autosave both still have a job to do.
        Assert.True(store.IsDirty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (ProjectStore store, ClipStore clips) CreateStore(
        Dictionary<string, string> sharedStorage, bool storageRefuses = false)
    {
        var opts     = Options.Create(new VideoEditorOptions());
        var clips    = new ClipStore(opts);
        var js       = new FakeLocalStorageJsRuntime(sharedStorage) { Refuse = storageRefuses };
        var errorLog = new ErrorLogService();
        var motion   = new MotionKeyframeService();
        var projSvc  = new ProjectService(clips, motion, js, new NoOpHttpClientFactory(), opts);
        var opfs     = new OPFSService(js, errorLog); // IsAvailable stays false — InitAsync() never called
        var ffmpeg   = new FfmpegService(js, errorLog, new MemFsLedger(), new WorkerWatchdog());
        var mounter  = new SourceMounter(ffmpeg, opfs);

        var store = new ProjectStore(clips, projSvc, opfs, ffmpeg, mounter, motion, js, errorLog);
        return (store, clips);
    }

    private sealed class NoOpHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static VideoClip MakeClip(string name = "clip.mp4") => new()
    {
        Id               = Guid.NewGuid(),
        Name             = name,
        TimelinePosition = 0,
        Duration         = 5.0,
        Order            = 0,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_ThenRestoreOnFreshInstance_RepopulatesClipStore()
    {
        var sharedStorage = new Dictionary<string, string>();

        // "Tab 1": build a project, save it.
        var (storeA, clipsA) = CreateStore(sharedStorage);
        await storeA.InitAsync();
        clipsA.PrimaryVideoTrack!.Items.Add(MakeClip("first.mp4"));
        await storeA.SaveAsync("My Project");

        // "Reload": brand-new ClipStore + ProjectStore, same backing storage.
        var (storeB, clipsB) = CreateStore(sharedStorage);
        await storeB.InitAsync();
        Assert.Empty(clipsB.PrimaryVideoTrack!.Items); // sanity: nothing restored yet

        await storeB.RestoreLastActiveAsync();

        Assert.Single(clipsB.PrimaryVideoTrack.Items);
        Assert.Equal("first.mp4", clipsB.PrimaryVideoTrack.Items[0].Name);
        Assert.Equal("My Project", storeB.CurrentProjectName);
        Assert.False(storeB.IsDirty);
    }

    [Fact]
    public async Task RestoreLastActiveAsync_NoPriorSave_DoesNothingAndDoesNotThrow()
    {
        var (store, clips) = CreateStore([]);
        await store.InitAsync();

        await store.RestoreLastActiveAsync();

        Assert.Empty(clips.PrimaryVideoTrack!.Items);
        Assert.Null(store.CurrentLocalId);
    }

    [Fact]
    public async Task RestoreLastActiveAsync_PointerToNonExistentProject_DoesNothingAndDoesNotThrow()
    {
        // Simulates a dangling pointer (e.g. project removed by some path other than
        // DeleteAsync, or from another tab) — the index has no entry for this id.
        var sharedStorage = new Dictionary<string, string> { ["bv-proj-active"] = Guid.NewGuid().ToString() };
        var (store, clips) = CreateStore(sharedStorage);

        await store.InitAsync();
        await store.RestoreLastActiveAsync();

        Assert.Empty(clips.PrimaryVideoTrack!.Items);
        Assert.Null(store.CurrentLocalId);
    }

    [Fact]
    public async Task NewProjectAsync_ClearsActivePointer_SoNextRestoreDoesNothing()
    {
        var sharedStorage = new Dictionary<string, string>();

        var (storeA, clipsA) = CreateStore(sharedStorage);
        await storeA.InitAsync();
        clipsA.PrimaryVideoTrack!.Items.Add(MakeClip());
        await storeA.SaveAsync("Saved then abandoned");

        await storeA.NewProjectAsync(); // same instance, but simulates "started a new project"

        // A subsequent reload should NOT bring the old project back, since it's no longer "active."
        var (storeB, clipsB) = CreateStore(sharedStorage);
        await storeB.InitAsync();
        await storeB.RestoreLastActiveAsync();

        Assert.Empty(clipsB.PrimaryVideoTrack!.Items);
    }

    [Fact]
    public async Task DeleteAsync_OfActiveProject_ClearsActivePointer_SoNextRestoreDoesNothing()
    {
        var sharedStorage = new Dictionary<string, string>();

        var (storeA, clipsA) = CreateStore(sharedStorage);
        await storeA.InitAsync();
        clipsA.PrimaryVideoTrack!.Items.Add(MakeClip());
        await storeA.SaveAsync("Will be deleted");
        var id = storeA.CurrentLocalId!.Value;

        await storeA.DeleteAsync(id);

        var (storeB, clipsB) = CreateStore(sharedStorage);
        await storeB.InitAsync();
        await storeB.RestoreLastActiveAsync();

        Assert.Empty(clipsB.PrimaryVideoTrack!.Items);
    }

    [Fact]
    public async Task OpenAsync_UpdatesActivePointer_SoASubsequentReloadRestoresTheOpenedProject()
    {
        var sharedStorage = new Dictionary<string, string>();

        // Save two different projects from the same "tab".
        var (storeA, clipsA) = CreateStore(sharedStorage);
        await storeA.InitAsync();
        clipsA.PrimaryVideoTrack!.Items.Add(MakeClip("proj-one.mp4"));
        await storeA.SaveAsync("Project One");
        var idOne = storeA.CurrentLocalId!.Value;

        await storeA.NewProjectAsync();
        clipsA.Reset();
        clipsA.PrimaryVideoTrack!.Items.Add(MakeClip("proj-two.mp4"));
        await storeA.SaveAsync("Project Two");

        // Explicitly re-open Project One (as if via File > Open).
        await storeA.OpenAsync(idOne);

        // A reload should now bring back Project One, not Project Two.
        var (storeB, clipsB) = CreateStore(sharedStorage);
        await storeB.InitAsync();
        await storeB.RestoreLastActiveAsync();

        Assert.Single(clipsB.PrimaryVideoTrack!.Items);
        Assert.Equal("proj-one.mp4", clipsB.PrimaryVideoTrack.Items[0].Name);
    }
}
