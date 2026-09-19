using Ben.Video.Editor.Models;
using Ben.Video.Editor.Models.Assets;
using Ben.Video.Editor.Services;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// Clip art whose artwork is not on this machine says so.
/// </summary>
/// <remarks>
/// <para>Clip art had footage's problem and none of its repair. Its asset source is documented as
/// the key to re-downloading the file and nothing ever used it, so at export the layer was left
/// out, the preview drew nothing, and the timeline chip looked entirely normal — because clip
/// art's missing-media flag was never set either (2026-09-05 audit, callouts-14).</para>
///
/// <para>The chip's warning already had a place to appear. It just never had anything to say.</para>
/// </remarks>
public sealed class ClipArtRelinkTests
{
    /// <summary>A browser with no usable storage — so nothing is ever found locally.</summary>
    private sealed class NoStorage : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken ct, object?[]? args)
            => throw new InvalidOperationException("This browser has no origin-private filesystem.");
    }

    private static MediaRelinkService Service()
    {
        var errorLog = new ErrorLogService();
        var opfs     = new OPFSService(new NoStorage(), errorLog);
        var ffmpeg   = new FfmpegService(new NoStorage(), errorLog, new MemFsLedger(), new WorkerWatchdog());

        return new MediaRelinkService(opfs, new SourceMounter(ffmpeg, opfs), ffmpeg, errorLog);
    }

    private static ClipArtClip Art(string? assetId = null) => new()
    {
        Name        = "arrow",
        Duration    = 3,
        AssetId     = assetId ?? Guid.NewGuid().ToString(),
        AssetFormat = VideoAssetFormat.Svg,
        AssetSource = AssetSource.SharedCatalog,
    };

    [Fact]
    public async Task Artwork_that_cannot_be_fetched_is_marked_missing()
    {
        var art = Art();

        await Service().RestoreClipArtAsync([art]);

        Assert.True(art.IsMediaMissing);
    }

    [Fact]
    public async Task Nothing_is_reported_as_restored_when_nothing_was()
    {
        var restored = await Service().RestoreClipArtAsync([Art(), Art()]);

        Assert.Equal(0, restored);
    }

    /// <summary>
    /// A clip whose asset id is not an id at all is left alone rather than flagged. It has never
    /// pointed at a file, so "missing" would be the wrong word for it.
    /// </summary>
    [Fact]
    public async Task Art_with_no_real_asset_id_is_left_as_it_was()
    {
        var art = Art(assetId: "not-a-guid");

        await Service().RestoreClipArtAsync([art]);

        Assert.False(art.IsMediaMissing);
    }

    [Fact]
    public async Task Nothing_to_restore_is_not_an_error()
        => Assert.Equal(0, await Service().RestoreClipArtAsync([]));
}
