using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Backlog #29 regression tests: <see cref="FfmpegService.OnFfmpegLog"/> was previously a no-op that
/// discarded every ffmpeg log line, so a failed command could only ever report a bare exit code (and
/// before phase 75, not even that — exit codes were discarded too, which is how an export could
/// report success while producing a video-less file).
/// </summary>
public sealed class FfmpegServiceLogTests
{
    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => throw new NotSupportedException();
    }

    private static FfmpegService CreateService() => new(new FakeJsRuntime(), new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());

    [Fact]
    public void OnFfmpegLog_CapturesLines_OldestFirst()
    {
        var svc = CreateService();
        svc.OnFfmpegLog("line one");
        svc.OnFfmpegLog("line two");

        Assert.Equal(["line one", "line two"], svc.LogTail);
    }

    [Fact]
    public void OnFfmpegLog_CapsAtCapacity_DroppingOldest()
    {
        var svc = CreateService();
        for (var i = 0; i < 50; i++)
            svc.OnFfmpegLog($"line {i}");

        Assert.Equal(40, svc.LogTail.Count);
        Assert.Equal("line 10", svc.LogTail.First());
        Assert.Equal("line 49", svc.LogTail.Last());
    }

    // The single-threaded ffmpeg.wasm core prints a benign "Aborted()" line as part of every
    // command's normal exit path (verified live: imports and exports succeed right through
    // them). Log capture must record it like any other line — and nothing anywhere should
    // treat it as a crash signal; the exit code each command returns is the failure signal.
    [Fact]
    public void OnFfmpegLog_BenignAbortedLine_IsJustCaptured()
    {
        var svc = CreateService();
        svc.OnFfmpegLog("Aborted()");

        Assert.Equal(["Aborted()"], svc.LogTail);
        Assert.Equal(FfmpegState.Idle, svc.State); // unchanged — no crash handling triggered
    }

    // Item #9 live-verification regression: RenderWorkerBackend.ResolveSourceAsync used to call
    // ReadFileAsync directly, whose EnsureReady() guard throws a raw "FfmpegService is not ready"
    // InvalidOperationException whenever background rendering loses the race for the shared main
    // instance to an in-flight export. That exception propagated straight into the user-visible
    // error log for what's actually expected, transient contention. ReadFileWhenReadyAsync (mirrors
    // the existing WriteFileWhenReadyAsync) waits instead of throwing — it must never let that
    // InvalidOperationException escape, and it must never touch the JS module while State isn't
    // Ready (the FakeJsRuntime throws NotSupportedException on any invoke, which would fail this
    // test if it did).
    [Fact]
    public async Task ReadFileWhenReadyAsync_NeverReady_WaitsRatherThanThrowing()
    {
        var svc = CreateService(); // State stays Idle forever — never calls into the JS module
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(600)); // survives at least one 250ms poll

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.ReadFileWhenReadyAsync("whatever.mp4", cts.Token));
    }
}
