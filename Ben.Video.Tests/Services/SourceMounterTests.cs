using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The actual WORKERFS mount/unmount JS behavior is browser-only and verified live (see
/// README-phase-118.md) — these tests cover the C#-testable fallback and bookkeeping logic, using
/// the same NoOpJSRuntime fake established in KeyboardShortcutServiceTests/GoogleFontServiceTests:
/// every InvokeAsync call returns default(TValue), which drives both FfmpegService and OPFSService
/// into their own "not available" states, letting SourceMounter's graceful-degradation path prove
/// itself without a real browser.
/// </summary>
public sealed class SourceMounterTests
{
    private static SourceMounter CreateMounter()
    {
        var js = new NoOpJSRuntime();
        var errorLog = new ErrorLogService();
        return new SourceMounter(new FfmpegService(js, errorLog, new MemFsLedger(), new WorkerWatchdog()), new OPFSService(js, errorLog));
    }

    [Fact]
    public async Task MountAsync_OpfsUnavailable_ReturnsNull()
    {
        var mounter = CreateMounter();
        var result = await mounter.MountAsync(Guid.NewGuid(), ".mp4");
        Assert.Null(result);
    }

    [Fact]
    public async Task UnmountAsync_NeverMounted_DoesNotThrow()
    {
        var mounter = CreateMounter();
        await mounter.UnmountAsync(Guid.NewGuid()); // should no-op, not throw
    }

    [Fact]
    public async Task RemountAllAsync_NothingTracked_ReturnsEmptyAndDoesNotThrow()
    {
        var mounter = CreateMounter();
        var result = await mounter.RemountAllAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task RemountAllAsync_AfterFailedMount_HasNothingToRemount()
    {
        // A failed MountAsync (OPFS unavailable, per the test above) must never register the clip
        // as "mounted" — otherwise RemountAllAsync would keep retrying a clip that was never
        // actually mounted in the first place.
        var mounter = CreateMounter();
        var clipId = Guid.NewGuid();
        await mounter.MountAsync(clipId, ".mp4");

        var remounted = await mounter.RemountAllAsync();
        Assert.Empty(remounted);
    }

    private sealed class NoOpJSRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            System.Threading.CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);
    }
}
