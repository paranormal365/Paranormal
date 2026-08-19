using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Item #70 phase 161 — revoke routing for preview blob URLs.
///
/// <para>Since phase 161 a preview URL can come from two places: the ffmpeg worker (MEMFS-backed)
/// or <c>sidecarInterop.js fetchAsBlobUrl</c> (no MEMFS at all). Sending a sidecar URL through the
/// worker route still <i>works</i> — <c>URL.revokeObjectURL</c> doesn't care about origin — but it
/// takes the phase-142 worker mutex to do it, queueing a one-line cleanup behind whatever encode
/// owns the worker. That's the coupling this whole arc removes, so these tests assert the route,
/// not just the outcome.</para>
/// </summary>
public sealed class PreviewUrlRevokerTests
{
    private sealed class RecordingModule : IJSObjectReference
    {
        public List<(string Identifier, object?[]? Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args));
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RoutingJsRuntime : IJSRuntime
    {
        public RecordingModule FfmpegModule { get; } = new();
        public RecordingModule SidecarModule { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "benImportEditorModule" && args?[0] is string path)
            {
                if (path.Contains("ffmpegInterop")) return ValueTask.FromResult((TValue)(object)FfmpegModule);
                if (path.Contains("sidecarInterop")) return ValueTask.FromResult((TValue)(object)SidecarModule);
            }
            throw new NotSupportedException($"unexpected top-level invoke: {identifier}");
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private static async Task<(PreviewUrlRevoker Revoker, RoutingJsRuntime Js)> CreateAsync()
    {
        var js = new RoutingJsRuntime();
        var ffmpeg = new FfmpegService(js, new ErrorLogService(), new MemFsLedger(), new WorkerWatchdog());
        await ffmpeg.LoadAsync(); // so RevokePreviewUrlAsync has a module to call through
        return (new PreviewUrlRevoker(ffmpeg, js, new ErrorLogService()), js);
    }

    [Fact]
    public async Task UnregisteredUrl_GoesThroughTheFfmpegWorker()
    {
        // The default matters: every preview URL that existed before this phase is worker-origin,
        // so defaulting there keeps all pre-existing callers correct without annotating each one.
        var (revoker, js) = await CreateAsync();

        Assert.Equal(PreviewUrlOrigin.FfmpegWorker, revoker.OriginOf("blob:worker-made"));
        await revoker.RevokeAsync("blob:worker-made");

        Assert.Contains(js.FfmpegModule.Calls, c => c.Identifier == "revokePreviewUrl");
        Assert.DoesNotContain(js.SidecarModule.Calls, c => c.Identifier == "revokeBlobUrl");
    }

    [Fact]
    public async Task RegisteredSidecarUrl_GoesThroughJsAndNeverTouchesTheWorker()
    {
        var (revoker, js) = await CreateAsync();
        revoker.RegisterSidecarUrl("blob:sidecar-made");

        Assert.Equal(PreviewUrlOrigin.Sidecar, revoker.OriginOf("blob:sidecar-made"));
        await revoker.RevokeAsync("blob:sidecar-made");

        Assert.Contains(js.SidecarModule.Calls, c => c.Identifier == "revokeBlobUrl");
        // The point of the whole exercise: no worker mutex taken for a cleanup that doesn't need it.
        Assert.DoesNotContain(js.FfmpegModule.Calls, c => c.Identifier == "revokePreviewUrl");
    }

    [Fact]
    public async Task RevokingASidecarUrlTwice_FallsBackToTheWorkerRouteNotADoubleJsRevoke()
    {
        // After the first revoke the registration is consumed, so a stray second call is treated
        // as an ordinary (unknown) url. Documents the actual behavior rather than pretending
        // double-revokes can't happen — phase 144 found that they can.
        var (revoker, js) = await CreateAsync();
        revoker.RegisterSidecarUrl("blob:sidecar-made");

        await revoker.RevokeAsync("blob:sidecar-made");
        await revoker.RevokeAsync("blob:sidecar-made");

        Assert.Single(js.SidecarModule.Calls, c => c.Identifier == "revokeBlobUrl");
        Assert.Contains(js.FfmpegModule.Calls, c => c.Identifier == "revokePreviewUrl");
    }

    [Theory]
    [InlineData("")]
    public async Task EmptyUrl_IsIgnoredEntirely(string url)
    {
        var (revoker, js) = await CreateAsync();

        await revoker.RevokeAsync(url);

        Assert.DoesNotContain(js.SidecarModule.Calls, c => c.Identifier == "revokeBlobUrl");
        Assert.DoesNotContain(js.FfmpegModule.Calls, c => c.Identifier == "revokePreviewUrl");
    }

    [Fact]
    public async Task RegisteringEmptyUrl_DoesNotPoisonTheRegistry()
    {
        var (revoker, _) = await CreateAsync();
        revoker.RegisterSidecarUrl("");

        Assert.Equal(PreviewUrlOrigin.FfmpegWorker, revoker.OriginOf(""));
    }
}
