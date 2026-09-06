using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 143 — covers the two state-machine/recovery fixes
/// that needed a working fake module (same shape as <see cref="FfmpegServiceDiagnosticsTests"/>'s
/// and <see cref="FfmpegServiceConcurrencyTests"/>'s own fakes, duplicated locally rather than
/// shared since each file's fake has slightly different configurability needs):
/// <c>ExtractAudioAsync</c>'s nested-state bug, and <c>SourceMounter.RemountAllAsync</c>'s
/// drop-on-any-failure bug.
/// </summary>
public sealed class FfmpegServiceRecoveryTests
{
    private sealed class ConfigurableFakeModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public HashSet<string> ThrowingIdentifiers { get; } = [];
        public Dictionary<string, object?> Results { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (ThrowingIdentifiers.Contains(identifier))
                throw new InvalidOperationException($"simulated failure for {identifier}");
            if (Results.TryGetValue(identifier, out var result))
                return ValueTask.FromResult((TValue)result!);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MultiModuleFakeJsRuntime : IJSRuntime
    {
        public ConfigurableFakeModule FfmpegModule { get; } = new();
        public ConfigurableFakeModule OpfsModule { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "benImportEditorModule" && args?[0] is string path)
            {
                if (path.Contains("ffmpegInterop")) return ValueTask.FromResult((TValue)(object)FfmpegModule);
                if (path.Contains("opfsInterop")) return ValueTask.FromResult((TValue)(object)OpfsModule);
            }
            throw new NotSupportedException($"unexpected top-level invoke: {identifier}");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    // ── A failed command must not leave the service pinned at Processing ─────
    // Backlog item 94. A background render froze at a percentage with Export disabled behind it
    // and no ffmpeg operation in flight — the state machine had entered Processing and never
    // come out, because every escape from a command that fails (a throw from the module, or a
    // non-zero exit code) bypassed the SetState(Ready) that only sat on the success path.
    //
    // A failing command is ordinary — a bad filter, a stream that is not there — and the core
    // survives it. What must not survive it is a status chip that says "Processing… 64%" forever.

    [Fact]
    public async Task ExecAsync_WhenTheModuleThrows_ReturnsToReady()
    {
        var js = new MultiModuleFakeJsRuntime();
        js.FfmpegModule.ThrowingIdentifiers.Add("exec");

        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecAsync(["-i", "in.mp4", "out.mp4"]));

        Assert.NotEqual(FfmpegState.Processing, svc.State);
    }

    [Fact]
    public async Task ExecAsync_WhenFfmpegExitsNonZero_ReturnsToReady()
    {
        var js = new MultiModuleFakeJsRuntime();
        js.FfmpegModule.Results["exec"] = 1;          // ffmpeg refused the command

        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => svc.ExecAsync(["-i", "in.mp4", "-map", "0:a", "out.mp4"]));

        Assert.NotEqual(FfmpegState.Processing, svc.State);
    }

    [Fact]
    public async Task ConcatCopyAsync_WhenFfmpegExitsNonZero_ReturnsToReady()
    {
        var js = new MultiModuleFakeJsRuntime();
        js.FfmpegModule.Results["concatCopy"] = 1;

        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => svc.ConcatCopyAsync(["a.mp4", "b.mp4"], "out.mp4"));

        Assert.NotEqual(FfmpegState.Processing, svc.State);
    }

    // ── A crashed engine has to announce itself ──────────────────────────────
    // 2026-09-05 audit, F7. Every failure went to Error and stayed there until somebody pressed
    // Initialize again, and nothing said so — the preview stopped refreshing, exports refused to
    // start, and the only clue was a status chip most people never look at. A trap and a bad
    // command are not the same thing, and the difference decides whether the editor can put itself
    // right.

    private sealed class ThrowingModule : IJSObjectReference
    {
        public required Exception Failure { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "exec") throw Failure;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SingleModuleJsRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult((TValue)module);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private static async Task<(FfmpegService Service, List<WorkerFailureKind> Crashes)>
        RunFailingExecAsync(Exception failure)
    {
        var js  = new SingleModuleJsRuntime(new ThrowingModule { Failure = failure });
        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        var crashes = new List<WorkerFailureKind>();
        svc.OnWorkerCrashed += kind => crashes.Add(kind);

        await Assert.ThrowsAnyAsync<Exception>(() => svc.ExecAsync(["-i", "in.mp4", "out.mp4"]));
        return (svc, crashes);
    }

    [Fact]
    public async Task RecordFailure_WasmRuntimeError_RaisesOnWorkerCrashed()
    {
        var (svc, crashes) = await RunFailingExecAsync(
            new InvalidOperationException("RuntimeError: memory access out of bounds"));

        Assert.Equal(WorkerFailureKind.Crashed, svc.LastFailureKind);
        Assert.Equal([WorkerFailureKind.Crashed], crashes);
    }

    [Fact]
    public async Task RecordFailure_OutOfMemory_IsToldApartFromATrap()
    {
        var (svc, crashes) = await RunFailingExecAsync(
            new InvalidOperationException("Aborted(). Cannot enlarge memory arrays"));

        Assert.Equal(WorkerFailureKind.OutOfMemory, svc.LastFailureKind);
        Assert.Equal([WorkerFailureKind.OutOfMemory], crashes);
    }

    /// <summary>
    /// An ordinary failure must not set off a restart. Restarting the engine every time a filter
    /// argument is wrong would be its own kind of broken.
    /// </summary>
    [Fact]
    public async Task RecordFailure_AnOrdinaryError_RaisesNothing()
    {
        var (svc, crashes) = await RunFailingExecAsync(
            new InvalidOperationException("No such file or directory"));

        Assert.Equal(WorkerFailureKind.Recoverable, svc.LastFailureKind);
        Assert.Empty(crashes);
    }

    [Fact]
    public async Task ResetWorkerAsync_ClearsTheFailureAndTheWedge()
    {
        var (svc, _) = await RunFailingExecAsync(
            new InvalidOperationException("RuntimeError: unreachable"));

        await svc.ResetWorkerAsync();

        Assert.Equal(WorkerFailureKind.Recoverable, svc.LastFailureKind);
        Assert.False(svc.IsWorkerWedged);
        Assert.Equal(FfmpegState.Idle, svc.State);
    }

    // ── ExtractAudioAsync nested-state bug ───────────────────────────────────

    [Fact]
    public async Task ExtractAudioAsync_CommandFailure_ResolvesToReadyNotError()
    {
        var js = new MultiModuleFakeJsRuntime();
        js.FfmpegModule.Results["exec"] = 1; // non-zero exit — a normal failed command, not a crash
        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExtractAudioAsync("in.mp4", "out.aac"));

        // The actual bug: this used to unconditionally end up Error, forcing a full reload for
        // what's just an incompatible-codec-style command failure the core survives fine.
        Assert.Equal(FfmpegState.Ready, svc.State);
    }

    [Fact]
    public async Task ExtractAudioAsync_JsException_StillResolvesToError()
    {
        // The fix must not over-correct — a genuine worker-level exception is still Error.
        var js = new MultiModuleFakeJsRuntime();
        js.FfmpegModule.ThrowingIdentifiers.Add("exec");
        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExtractAudioAsync("in.mp4", "out.aac"));

        Assert.Equal(FfmpegState.Error, svc.State);
    }

    [Fact]
    public async Task ExtractAudioAsync_Success_ResolvesToReady()
    {
        var js = new MultiModuleFakeJsRuntime();
        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await svc.LoadAsync();

        await svc.ExtractAudioAsync("in.mp4", "out.aac");

        Assert.Equal(FfmpegState.Ready, svc.State);
    }

    // ── SourceMounter.RemountAllAsync drop-on-failure bug ────────────────────

    [Fact]
    public async Task RemountAllAsync_MountFailsButOpfsCopyStillExists_KeepsTrackingForNextAttempt()
    {
        var js = new MultiModuleFakeJsRuntime();
        js.OpfsModule.Results["opfsIsAvailable"] = true;
        js.OpfsModule.Results["opfsReadAsFile"] = new ConfigurableFakeModule(); // stand-in File ref
        js.FfmpegModule.Results["mountWorkerFs"] = "/src_test/clip.mp4";

        var errorLog = new ErrorLogService();
        var ffmpeg = new FfmpegService(js, errorLog, new MemFsLedger(), new WorkerWatchdog());
        await ffmpeg.LoadAsync(); // must be Ready-enough to set _module before any mount can work
        var opfs = new OPFSService(js, errorLog);
        var mounter = new SourceMounter(ffmpeg, opfs);

        var clipId = Guid.NewGuid();
        var mounted = await mounter.MountAsync(clipId, ".mp4");
        Assert.NotNull(mounted); // sanity: it's actually tracked to begin with

        // Simulate the EEXIST-style transient failure (or any other mount hiccup) — the OPFS copy
        // itself is completely fine; only this one mount attempt fails.
        js.FfmpegModule.ThrowingIdentifiers.Add("mountWorkerFs");
        var failedAttempt = await mounter.RemountAllAsync();
        Assert.Empty(failedAttempt);

        // The actual bug: before the fix, that failure alone permanently dropped the clip from
        // tracking. Prove it's still tracked by letting a later attempt succeed.
        js.FfmpegModule.ThrowingIdentifiers.Remove("mountWorkerFs");
        var secondAttempt = await mounter.RemountAllAsync();
        Assert.True(secondAttempt.ContainsKey(clipId));
    }

    [Fact]
    public async Task RemountAllAsync_OpfsCopyGenuinelyGone_DropsTracking()
    {
        // The one case that SHOULD permanently drop tracking — contrast with the test above.
        var js = new MultiModuleFakeJsRuntime();
        js.OpfsModule.Results["opfsIsAvailable"] = true;
        js.OpfsModule.Results["opfsReadAsFile"] = new ConfigurableFakeModule();
        js.FfmpegModule.Results["mountWorkerFs"] = "/src_test/clip.mp4";

        var errorLog = new ErrorLogService();
        var ffmpeg = new FfmpegService(js, errorLog, new MemFsLedger(), new WorkerWatchdog());
        await ffmpeg.LoadAsync(); // must be Ready-enough to set _module before any mount can work
        var opfs = new OPFSService(js, errorLog);
        var mounter = new SourceMounter(ffmpeg, opfs);

        var clipId = Guid.NewGuid();
        var mounted = await mounter.MountAsync(clipId, ".mp4");
        Assert.NotNull(mounted);

        js.OpfsModule.Results["opfsReadAsFile"] = null; // OPFS copy is now confirmed gone
        var remounted = await mounter.RemountAllAsync();
        Assert.Empty(remounted);

        // Now even a working mount can't bring it back — it was correctly dropped.
        js.OpfsModule.Results["opfsReadAsFile"] = new ConfigurableFakeModule();
        var secondAttempt = await mounter.RemountAllAsync();
        Assert.False(secondAttempt.ContainsKey(clipId));
    }

    // ── Watchdog wiring integration (short poll interval so this doesn't need a real 45s wait) ──

    private sealed class GatedFakeModule : IJSObjectReference
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _gate.TrySetResult();

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "exec") await _gate.Task; // never completes until released
            return default!;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class GatedFakeJsRuntime : IJSRuntime
    {
        public GatedFakeModule Module { get; } = new();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => identifier == "benImportEditorModule"
                ? ValueTask.FromResult((TValue)(object)Module)
                : throw new NotSupportedException(identifier);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    /// <summary>
    /// How long a wedge signal may take before we call it a genuine failure.
    /// </summary>
    /// <remarks>
    /// Generous on purpose, and it costs nothing in the passing case: the test awaits the event, so
    /// it finishes the moment the watchdog fires (tens of milliseconds) and this budget only comes
    /// into play when the watchdog never fires at all. The earlier version polled a 2-second clock
    /// instead, which made a busy machine indistinguishable from a broken watchdog — it failed in
    /// two of six full-solution runs on 2026-08-16 while passing every time in isolation.
    /// </remarks>
    private static readonly TimeSpan WedgeSignalBudget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Awaits <paramref name="signal"/>, failing with a readable message rather than a bare
    /// timeout if it never arrives.
    /// </summary>
    private static async Task AwaitSignalAsync(Task signal, string whatWasExpected)
    {
        var winner = await Task.WhenAny(signal, Task.Delay(WedgeSignalBudget));
        Assert.True(winner == signal, $"Timed out after {WedgeSignalBudget.TotalSeconds:0}s: {whatWasExpected}.");
        await signal; // surface any exception the signal itself carried
    }

    [Fact]
    public async Task Watchdog_GenuinelyStuckCommand_FlagsIsWorkerWedged()
    {
        var js = new GatedFakeJsRuntime();
        var watchdog = new WorkerWatchdog(TimeSpan.FromMilliseconds(30));
        var svc = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), watchdog,
            watchdogPollInterval: TimeSpan.FromMilliseconds(10));
        await svc.LoadAsync();

        // Subscribed before the command starts, so the signal cannot be missed by arriving early.
        var wedged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.OnWorkerWedged += () => wedged.TrySetResult();

        var stuckTask = svc.ExecAsync(["-i", "a.mp4"]); // never resolves until the gate releases

        await AwaitSignalAsync(wedged.Task, "the watchdog never reported the worker as wedged");

        // The event fired; the flag it is supposed to accompany must agree.
        Assert.True(svc.IsWorkerWedged);

        js.Module.Release(); // the gate finally opens — proves the watchdog flag didn't kill anything
        var code = await stuckTask;

        Assert.Equal(0, code);
        Assert.Equal(FfmpegState.Ready, svc.State);
        Assert.False(svc.IsWorkerWedged); // CommandFinished clears it once the command actually ends
    }
}
