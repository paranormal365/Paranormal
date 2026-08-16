using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #59-#65 flakiness investigation, phase 141 — covers the new diagnostics surface added to
/// <see cref="FfmpegService"/>: <see cref="FfmpegService.OperationTrace"/>, the
/// <see cref="ErrorLogService"/> wiring on every failure path (previously not injected at all —
/// failures were invisible outside a thrown exception's own message), <see cref="MemFsLedger"/>
/// wiring on every MEMFS-touching call, and the <see cref="FfmpegService.ProgressPercent"/> reset
/// on entering <see cref="FfmpegState.Processing"/>.
///
/// Unlike <see cref="FfmpegServiceLogTests"/> (which deliberately never reaches Ready — a
/// NotSupportedException-throwing fake is enough to prove the "wait, don't call the module" and
/// "never got there" contracts), these tests need a fake module that actually answers calls, since
/// the write/delete/rename/exec paths all require <c>EnsureReady()</c> to pass.
/// </summary>
public sealed class FfmpegServiceDiagnosticsTests
{
    /// <summary>A controllable fake for the JS module <c>FfmpegService</c> imports and calls
    /// every operation through. Records every call by identifier; any identifier added to
    /// <see cref="ThrowingIdentifiers"/> throws instead of resolving, simulating a JS-side/worker
    /// failure exactly like the real <c>ffmpegInterop.js</c> would surface one.</summary>
    private sealed class FakeModule : IJSObjectReference
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

    private sealed class FakeJsRuntime : IJSRuntime
    {
        public FakeModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => identifier == "import"
                ? ValueTask.FromResult((TValue)(object)Module)
                : throw new NotSupportedException($"unexpected top-level invoke: {identifier}");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private static async Task<(FfmpegService Service, FakeJsRuntime Js, ErrorLogService ErrorLog, MemFsLedger Ledger)> CreateReadyServiceAsync()
    {
        var js = new FakeJsRuntime();
        var errorLog = new ErrorLogService();
        var ledger = new MemFsLedger();
        var svc = new FfmpegService(js, errorLog, ledger, new WorkerWatchdog());
        await svc.LoadAsync();
        Assert.Equal(FfmpegState.Ready, svc.State); // sanity: every test below assumes this
        return (svc, js, errorLog, ledger);
    }

    // ── OperationTrace ───────────────────────────────────────────────────────

    [Fact]
    public async Task OperationTrace_SuccessfulCall_RecordsItWithSuccessTrue()
    {
        var (svc, _, _, _) = await CreateReadyServiceAsync(); // LoadAsync itself already traced "loadCore"

        await svc.WriteFileFromBytesAsync("clip.mp4", new byte[10]);

        var entry = svc.OperationTrace.Last();
        Assert.Equal("writeFileFromBytes", entry.Operation);
        Assert.True(entry.Success);
        Assert.True(entry.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task OperationTrace_FailedCall_RecordsItWithSuccessFalse()
    {
        var (svc, js, _, _) = await CreateReadyServiceAsync();
        js.Module.ThrowingIdentifiers.Add("exec");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecAsync(["-i", "a.mp4"]));

        var entry = svc.OperationTrace.Last();
        Assert.Equal("exec", entry.Operation);
        Assert.False(entry.Success);
    }

    [Fact]
    public async Task OperationTrace_CapsAtCapacity_DroppingOldest()
    {
        var (svc, _, _, _) = await CreateReadyServiceAsync();

        for (var i = 0; i < 250; i++)
            await svc.RevokePreviewUrlAsync($"blob:fake-{i}");

        Assert.Equal(200, svc.OperationTrace.Count); // matches LogTail's own capacity-test shape
    }

    // ── ErrorLogService wiring ───────────────────────────────────────────────

    [Fact]
    public async Task ExecAsync_JsException_LogsToErrorLogService()
    {
        var (svc, js, errorLog, _) = await CreateReadyServiceAsync();
        js.Module.ThrowingIdentifiers.Add("exec");

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecAsync(["-i", "a.mp4"]));

        Assert.Contains(errorLog.Entries, e => e.Source == "FfmpegService.ExecAsync");
        Assert.Equal(FfmpegState.Error, svc.State); // unchanged existing behavior
    }

    [Fact]
    public async Task ExecAsync_NonZeroExit_LogsToErrorLogService()
    {
        var (svc, js, errorLog, _) = await CreateReadyServiceAsync();
        js.Module.Results["exec"] = 1; // non-zero exit code, no exception

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExecAsync(["-i", "a.mp4"]));

        Assert.Contains(errorLog.Entries, e => e.Source == "FfmpegService.ffmpeg");
        Assert.Equal(FfmpegState.Ready, svc.State); // non-zero exit returns to Ready, not Error
    }

    [Fact]
    public async Task MountWorkerFsAsync_Failure_LogsButDoesNotThrow()
    {
        var (svc, js, errorLog, _) = await CreateReadyServiceAsync();
        js.Module.ThrowingIdentifiers.Add("mountWorkerFs");

        var result = await svc.MountWorkerFsAsync(js.Module, "/src_test");

        Assert.Null(result); // existing graceful-fallback contract, unchanged
        Assert.Contains(errorLog.Entries, e => e.Source == "FfmpegService.MountWorkerFsAsync");
    }

    // ── MemFsLedger wiring ───────────────────────────────────────────────────

    [Fact]
    public async Task WriteFileFromBytesAsync_TracksSizeInLedger()
    {
        var (svc, _, _, ledger) = await CreateReadyServiceAsync();

        await svc.WriteFileFromBytesAsync("clip.mp4", new byte[12_345]);

        Assert.Equal(12_345, ledger.TotalBytes);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public async Task WriteBytesAsync_TracksSizeInLedger()
    {
        var (svc, _, _, ledger) = await CreateReadyServiceAsync();

        await svc.WriteBytesAsync("temp.bin", new byte[500]);

        Assert.Equal(500, ledger.TotalBytes);
    }

    [Fact]
    public async Task DeleteFileAsync_UntracksFromLedger()
    {
        var (svc, _, _, ledger) = await CreateReadyServiceAsync();
        await svc.WriteFileFromBytesAsync("clip.mp4", new byte[100]);

        await svc.DeleteFileAsync("clip.mp4");

        Assert.Equal(0, ledger.TotalBytes);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public async Task RenameFileAsync_RenamesLedgerEntryInPlace()
    {
        var (svc, _, _, ledger) = await CreateReadyServiceAsync();
        await svc.WriteFileFromBytesAsync("preview_vid_0.mp4", new byte[7_000]);

        await svc.RenameFileAsync("preview_vid_0.mp4", "final.mp4");

        Assert.Equal(1, ledger.Count);
        Assert.Equal(7_000, ledger.TotalBytes);
        Assert.Equal("final.mp4", ledger.Entries.Single().Name);
    }

    [Fact]
    public async Task ExportToOpfsAsync_Success_UntracksTheMemFsCopy()
    {
        var (svc, js, _, ledger) = await CreateReadyServiceAsync();
        await svc.WriteFileFromBytesAsync("export.mp4", new byte[9_000]);
        js.Module.Results["exportToOpfs"] = 9_000L; // >= 0 means the JS side deleted the MEMFS copy

        var size = await svc.ExportToOpfsAsync("export.mp4", Guid.NewGuid(), ".mp4");

        Assert.Equal(9_000, size);
        Assert.Equal(0, ledger.TotalBytes); // untracked — matches the real deletion this models
    }

    [Fact]
    public async Task ExportToOpfsAsync_Failure_KeepsTheMemFsCopyTracked()
    {
        var (svc, js, _, ledger) = await CreateReadyServiceAsync();
        await svc.WriteFileFromBytesAsync("export.mp4", new byte[9_000]);
        js.Module.ThrowingIdentifiers.Add("exportToOpfs");

        var size = await svc.ExportToOpfsAsync("export.mp4", Guid.NewGuid(), ".mp4");

        Assert.Equal(-1, size);
        Assert.Equal(9_000, ledger.TotalBytes); // still there — caller falls back to direct MEMFS
    }

    [Fact]
    public async Task TerminateAsync_ClearsTheLedger()
    {
        var (svc, _, _, ledger) = await CreateReadyServiceAsync();
        await svc.WriteFileFromBytesAsync("clip.mp4", new byte[100]);

        await svc.TerminateAsync();

        Assert.Equal(0, ledger.Count);
        Assert.Equal(FfmpegState.Idle, svc.State);
    }

    // ── ProgressPercent reset ────────────────────────────────────────────────

    [Fact]
    public async Task EnteringProcessing_ResetsProgressPercentToZero()
    {
        var (svc, _, _, _) = await CreateReadyServiceAsync();
        svc.OnFfmpegProgress(77, 1.0); // leftover from a previous command
        Assert.Equal(77, svc.ProgressPercent);

        await svc.ExecAsync(["-i", "a.mp4"]); // enters Processing, then resolves back to Ready

        // Nothing re-raised progress during this fake call, so ending at 0 proves the reset fired
        // on entry rather than progress just happening to arrive at 0 on its own.
        Assert.Equal(0, svc.ProgressPercent);
    }
}
