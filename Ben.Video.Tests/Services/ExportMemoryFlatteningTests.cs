using Ben.Video.Editor.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #38 phase D: covers the graceful-fallback contract the new OPFS-backed export path
/// depends on. <see cref="ExportService"/> itself has no direct unit tests anywhere in this repo
/// (its pipeline is almost entirely JS-interop orchestration with no separable pure-C# seam) — the
/// pipeline changes are verified live instead (see README-phase-119.md). What's tested here is the
/// one piece of new branching logic that matters in isolation: when OPFS/ffmpeg's JS module isn't
/// available, the new methods must degrade to a sentinel/no-op rather than throw, so
/// RunPipelineAsync's OPFS-unavailable fallback branch is reachable rather than dead code.
/// </summary>
public sealed class ExportMemoryFlatteningTests
{
    private sealed class ThrowingJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task ExportToOpfsAsync_ModuleNeverLoaded_ReturnsNegativeOne()
    {
        var ffmpeg = new FfmpegService(new ThrowingJsRuntime(), new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        var result = await ffmpeg.ExportToOpfsAsync("out.mp4", Guid.NewGuid(), ".mp4");
        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task RenameFileAsync_ModuleNeverLoaded_DoesNotThrow()
    {
        var ffmpeg = new FfmpegService(new ThrowingJsRuntime(), new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await ffmpeg.RenameFileAsync("a.mp4", "b.mp4"); // should no-op, not throw
    }

    [Fact]
    public async Task DownloadBlobUrlAsync_ModuleNeverLoaded_DoesNotThrow()
    {
        var ffmpeg = new FfmpegService(new ThrowingJsRuntime(), new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await ffmpeg.DownloadBlobUrlAsync("blob:fake", "out.mp4"); // should no-op, not throw
    }

    [Fact]
    public async Task ReadExportAsBlobUrlAsync_OpfsUnavailable_ReturnsNull()
    {
        var opfs = new OPFSService(new ThrowingJsRuntime(), new ErrorLogService());
        var result = await opfs.ReadExportAsBlobUrlAsync(Guid.NewGuid(), ".mp4");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteExportAsync_OpfsUnavailable_DoesNotThrow()
    {
        var opfs = new OPFSService(new ThrowingJsRuntime(), new ErrorLogService());
        await opfs.DeleteExportAsync(Guid.NewGuid(), ".mp4"); // should no-op, not throw
    }
}
