using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 142 — proves <see cref="FfmpegService"/>'s new
/// worker lock actually serializes concurrent callers instead of letting them interleave on the
/// same worker (the confirmed root cause behind imports racing exports/auto-preview — see
/// phase 141's README for a live capture of exactly that happening), that the lock releases
/// correctly on both success and failure, that <see cref="FfmpegService.IsWorkerBusy"/> tracks
/// the wait-plus-run window accurately, and that <c>ExtractAudioAsync</c>'s internal reuse of the
/// exec path doesn't deadlock against the same (non-reentrant) lock it already holds.
/// </summary>
public sealed class FfmpegServiceConcurrencyTests
{
    /// <summary>Like <see cref="FfmpegServiceDiagnosticsTests"/>'s FakeModule, but calls for a
    /// "gated" identifier block until released — lets a test prove a second call never even
    /// reached the module while the first one is still in flight.</summary>
    private sealed class GatedFakeModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public List<object?[]?> ArgsByCall { get; } = [];
        public HashSet<string> ThrowingIdentifiers { get; } = [];
        private readonly Dictionary<string, TaskCompletionSource> _gates = new();

        public void Gate(string identifier) =>
            _gates[identifier] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string identifier) => _gates[identifier].TrySetResult();

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            ArgsByCall.Add(args);
            if (ThrowingIdentifiers.Contains(identifier))
                throw new InvalidOperationException($"simulated failure for {identifier}");
            if (_gates.TryGetValue(identifier, out var tcs)) await tcs.Task;
            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public GatedFakeModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => identifier == "benImportEditorModule"
                ? ValueTask.FromResult((TValue)(object)Module)
                : throw new NotSupportedException($"unexpected top-level invoke: {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private static async Task<(FfmpegService Service, FakeJsRuntime Js)> CreateReadyServiceAsync()
    {
        var js = new FakeJsRuntime();
        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();
        Assert.Equal(FfmpegState.Ready, svc.State);
        return (svc, js);
    }

    [Fact]
    public async Task TwoConcurrentExecCalls_SecondNeverReachesModuleUntilFirstReleasesTheLock()
    {
        var (svc, js) = await CreateReadyServiceAsync();
        js.Module.Gate("exec");

        var task1 = svc.ExecAsync(["-i", "a.mp4"]);
        await WaitUntilAsync(() => js.Module.Calls.Contains("exec"));

        var task2 = svc.ExecAsync(["-i", "b.mp4"]);
        await Task.Delay(50); // give task2 every chance to (wrongly) slip through if unserialized

        Assert.Single(js.Module.Calls, c => c == "exec"); // task2 hasn't reached the module at all
        Assert.True(svc.IsWorkerBusy);

        js.Module.Release("exec");
        await Task.WhenAll(task1, task2);

        Assert.Equal(2, js.Module.Calls.Count(c => c == "exec")); // task2 ran only after task1 released
        Assert.False(svc.IsWorkerBusy);
    }

    [Fact]
    public async Task IsWorkerBusy_TrueWhileInFlight_FalseBeforeAndAfter()
    {
        var (svc, js) = await CreateReadyServiceAsync();
        Assert.False(svc.IsWorkerBusy);

        js.Module.Gate("exec");
        var task = svc.ExecAsync(["-i", "a.mp4"]);
        await WaitUntilAsync(() => js.Module.Calls.Contains("exec"));

        Assert.True(svc.IsWorkerBusy);

        js.Module.Release("exec");
        await task;

        Assert.False(svc.IsWorkerBusy);
    }

    [Fact]
    public async Task LockIsReleasedOnThrow_SoASubsequentCallStillSucceeds()
    {
        var (svc, js) = await CreateReadyServiceAsync();
        js.Module.ThrowingIdentifiers.Add("exec");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecAsync(["-i", "a.mp4"]));
        Assert.Equal(FfmpegState.Error, svc.State);

        // Prove the semaphore wasn't leaked despite the throw: a fresh LoadAsync (State is Error,
        // not Ready/LoadingCore, so this actually re-runs rather than short-circuiting) and a
        // subsequent command must both succeed — either would hang forever if WithLockAsync's
        // finally hadn't run.
        js.Module.ThrowingIdentifiers.Remove("exec");
        await svc.LoadAsync();
        Assert.Equal(FfmpegState.Ready, svc.State);

        await svc.WriteFileFromBytesAsync("y.mp4", [1, 2, 3]);
        Assert.Equal(FfmpegState.Ready, svc.State);
    }

    [Fact]
    public async Task ExtractAudioAsync_DoesNotDeadlockAgainstItsOwnNonReentrantLock()
    {
        var (svc, _) = await CreateReadyServiceAsync();

        var task = svc.ExtractAudioAsync("in.mp4", "out.aac");
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.Same(task, completed); // if it deadlocked, Task.Delay would "win" instead
        Assert.Equal(FfmpegState.Ready, svc.State);
    }

    [Fact]
    public async Task ConcatClipsAsync_TwoCalls_UseDifferentListNames()
    {
        var (svc, js) = await CreateReadyServiceAsync();

        await svc.ConcatClipsAsync(["a.mp4", "b.mp4"], "out1.mp4");
        await svc.ConcatClipsAsync(["c.mp4", "d.mp4"], "out2.mp4");

        var listNames = js.Module.ArgsByCall
            .Zip(js.Module.Calls, (args, call) => (call, args))
            .Where(x => x.call == "concatClips")
            .Select(x => x.args![4] as string) // (segmentNames, outputName, w, h, listName)
            .ToList();

        Assert.Equal(2, listNames.Count);
        Assert.All(listNames, n => Assert.StartsWith("_concat_", n));
        Assert.NotEqual(listNames[0], listNames[1]);
    }

    [Fact]
    public async Task ConcatCopyAsync_TwoCalls_UseDifferentListNames()
    {
        var (svc, js) = await CreateReadyServiceAsync();

        await svc.ConcatCopyAsync(["a.mp4", "b.mp4"], "out1.mp4");
        await svc.ConcatCopyAsync(["c.mp4", "d.mp4"], "out2.mp4");

        var listNames = js.Module.ArgsByCall
            .Zip(js.Module.Calls, (args, call) => (call, args))
            .Where(x => x.call == "concatCopy")
            .Select(x => x.args![2] as string) // (segmentNames, outputName, listName)
            .ToList();

        Assert.Equal(2, listNames.Count);
        Assert.All(listNames, n => Assert.StartsWith("_concat_copy_", n));
        Assert.NotEqual(listNames[0], listNames[1]);
    }

    [Fact]
    public async Task QueuedImportStyleCall_RunsOnlyAfterAnInFlightExportFinishes()
    {
        // Live-verified in phase 141: an auto-preview concatClips ran concurrently with an
        // import's writeFileFromBytes on the same instance. This proves that's now impossible.
        var (svc, js) = await CreateReadyServiceAsync();
        js.Module.Gate("concatClips");

        var exportTask = svc.ConcatClipsAsync(["a.mp4"], "preview.mp4");
        await WaitUntilAsync(() => js.Module.Calls.Contains("concatClips"));

        var importTask = svc.WriteFileFromBytesAsync("imported.mp4", [1, 2, 3]);
        await Task.Delay(50);

        Assert.DoesNotContain("writeFileFromBytes", js.Module.Calls); // still queued behind the export

        js.Module.Release("concatClips");
        await Task.WhenAll(exportTask, importTask);

        Assert.Contains("writeFileFromBytes", js.Module.Calls);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, cts.Token);
        }
    }
}
