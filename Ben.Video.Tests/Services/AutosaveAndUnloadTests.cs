using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Whether closing the tab should ask first.
/// </summary>
/// <remarks>
/// Nothing asked. Closing the tab, or following a link out of the editor, took whatever was unsaved
/// with it — and a page that has not registered a handler gets no warning of its own, so there was
/// no moment at which anybody could have noticed (2026-09-05 audit, F9).
/// </remarks>
public sealed class UnloadGuardPolicyTests
{
    [Fact]
    public void A_clean_idle_project_does_not_interrupt_anybody()
    {
        Assert.False(UnloadGuardPolicy.ShouldGuard(
            hasUnsavedChanges: false, autosavePending: false, renderRunning: false));
    }

    [Fact]
    public void Unsaved_edits_are_worth_asking_about()
    {
        Assert.True(UnloadGuardPolicy.ShouldGuard(true, false, false));
    }

    /// <summary>
    /// An autosave that has been scheduled and has not run is work that is not in storage yet.
    /// </summary>
    [Fact]
    public void So_is_a_save_that_has_not_happened_yet()
    {
        Assert.True(UnloadGuardPolicy.ShouldGuard(false, true, false));
    }

    /// <summary>
    /// A render lives entirely in the tab, so leaving does not background it — it destroys it. And
    /// a long export is exactly the thing somebody wanders off during.
    /// </summary>
    [Fact]
    public void And_so_is_a_render_in_progress()
    {
        Assert.True(UnloadGuardPolicy.ShouldGuard(false, false, true));
    }

    [Fact]
    public void A_running_render_is_the_reason_given_even_alongside_unsaved_edits()
    {
        Assert.Contains("cancels it", UnloadGuardPolicy.Reason(hasUnsavedChanges: true, renderRunning: true));
    }

    [Fact]
    public void Otherwise_the_reason_names_the_unsaved_work()
    {
        Assert.Contains("unsaved changes", UnloadGuardPolicy.Reason(true, false));
        Assert.Contains("not finished saving", UnloadGuardPolicy.Reason(false, false));
    }
}

/// <summary>
/// The project writes itself shortly after editing stops.
/// </summary>
/// <remarks>
/// Nothing wrote anything unless somebody chose Save, so a reload, a crashed tab or a closed window
/// took the whole session with it — and the editor is exactly the kind of place people work in for
/// an hour without thinking about files (2026-09-05 audit, F9).
/// </remarks>
public sealed class AutosaveTests
{
    private static (ProjectStore Store, ClipStore Clips, Dictionary<string, string> Storage) Create()
    {
        var storage  = new Dictionary<string, string>();
        var opts     = Options.Create(new VideoEditorOptions());
        var clips    = new ClipStore(opts);
        var js       = new FakeStorageJsRuntime(storage);
        var errorLog = new ErrorLogService();
        var motion   = new MotionKeyframeService();
        var projSvc  = new ProjectService(clips, motion, js, new NoHttp(), opts);
        var opfs     = new OPFSService(js, errorLog);
        var ffmpeg   = new FfmpegService(js, errorLog, new MemFsLedger(), new WorkerWatchdog());
        var mounter  = new SourceMounter(ffmpeg, opfs);

        return (new ProjectStore(clips, projSvc, opfs, ffmpeg, mounter, motion, js, errorLog),
                clips, storage);
    }

    [Fact]
    public async Task Nothing_is_written_until_autosave_is_switched_on()
    {
        var (store, clips, storage) = Create();
        await store.InitAsync();

        clips.AddClip(new VideoClip { Name = "clip", Duration = 5 });
        await store.FlushAutosaveAsync();

        Assert.DoesNotContain(storage, kv => kv.Key.StartsWith("bv-proj-", StringComparison.Ordinal)
                                          && kv.Key != "bv-proj-index");
    }

    [Fact]
    public async Task Editing_then_settling_writes_the_project()
    {
        var (store, clips, storage) = Create();
        await store.InitAsync();
        store.EnableAutosave();

        clips.AddClip(new VideoClip { Name = "clip", Duration = 5 });

        // The debounce is real time; wait past it rather than reaching into the timer.
        await Task.Delay(ProjectStore.AutosaveIdle + TimeSpan.FromMilliseconds(400));

        Assert.False(store.IsDirty);
        Assert.NotNull(store.LastSavedAt);
        Assert.Contains(storage, kv => kv.Value.Contains("\"Name\": \"clip\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// A burst of edits writes once at the end, not once per edit.
    /// </summary>
    [Fact]
    public async Task A_burst_of_edits_settles_into_one_write()
    {
        var (store, clips, _) = Create();
        await store.InitAsync();
        store.EnableAutosave();

        for (var i = 0; i < 5; i++)
        {
            clips.AddClip(new VideoClip { Name = $"clip {i}", Duration = 5 });
            await Task.Delay(100);
        }

        // Still inside the idle window, so nothing has been written yet.
        Assert.True(store.IsDirty);

        await Task.Delay(ProjectStore.AutosaveIdle + TimeSpan.FromMilliseconds(400));
        Assert.False(store.IsDirty);
    }

    /// <summary>
    /// Opening a project is not editing it.
    /// </summary>
    /// <remarks>
    /// The store raises change events as it rebuilds the timeline. Treating those as edits would
    /// mark a freshly-opened project dirty and set autosave writing it straight back — which for a
    /// project opened from the server would quietly fork it into browser storage.
    /// </remarks>
    [Fact]
    public async Task Opening_a_project_does_not_mark_it_edited()
    {
        var (store, clips, _) = Create();
        await store.InitAsync();
        store.EnableAutosave();

        clips.AddClip(new VideoClip { Name = "clip", Duration = 5 });
        await store.SaveAsync("Saved");

        var file = new ProjectFile { ProjectName = "Reopened" };
        await store.LoadFromFileAsync(file, "Reopened");

        Assert.False(store.IsDirty);
        Assert.False(store.AutosavePending);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    private sealed class FakeStorageJsRuntime(Dictionary<string, string> storage) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => identifier == "benImportEditorModule"
                ? ValueTask.FromResult((TValue)(object)new Module(storage))
                : throw new NotSupportedException(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        private sealed class Module(Dictionary<string, string> storage) : IJSObjectReference
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            {
                switch (identifier)
                {
                    case "getItem" when args is [string key]:
                        return ValueTask.FromResult((TValue)(object?)(storage.TryGetValue(key, out var v) ? v : null)!);
                    case "setItem" when args is [string key, string value]:
                        storage[key] = value;
                        return Result<TValue>();
                    case "removeItem" when args is [string key]:
                        storage.Remove(key);
                        return Result<TValue>();
                    default:
                        return Result<TValue>();
                }
            }

            private static ValueTask<TValue> Result<TValue>() =>
                typeof(TValue) == typeof(bool)
                    ? ValueTask.FromResult((TValue)(object)true)
                    : default!;

            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
                => InvokeAsync<TValue>(identifier, args);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
