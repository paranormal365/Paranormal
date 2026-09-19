using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Ben.Video.Tests.Services;

/// <summary>
/// What counts as unsaved work.
/// </summary>
/// <remarks>
/// The dirty flag decides three things: whether Save is offered, whether autosave has anything to
/// write, and whether leaving the page asks first. Anything it does not notice is work that can be
/// lost without a word.
/// </remarks>
public sealed class ProjectDirtyTrackingTests
{
    private static (ProjectStore Store, ClipStore Clips, MotionKeyframeService Motion) Create()
    {
        var opts     = Options.Create(new VideoEditorOptions());
        var clips    = new ClipStore(opts);
        var js       = new NoJs();
        var errorLog = new ErrorLogService();
        var motion   = new MotionKeyframeService();
        var projSvc  = new ProjectService(clips, motion, js, new NoHttp(), opts);
        var opfs     = new OPFSService(js, errorLog);
        var ffmpeg   = new FfmpegService(js, errorLog, new MemFsLedger(), new WorkerWatchdog());
        var mounter  = new SourceMounter(ffmpeg, opfs);

        return (new ProjectStore(clips, projSvc, opfs, ffmpeg, mounter, motion, js, errorLog),
                clips, motion);
    }

    [Fact]
    public void A_new_store_has_nothing_to_save()
    {
        var (store, _, _) = Create();

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Editing_the_timeline_counts()
    {
        var (store, clips, _) = Create();

        clips.AddClip(new VideoClip { Name = "clip", Duration = 5 });

        Assert.True(store.IsDirty);
    }

    /// <summary>
    /// Animating a layer is editing the project.
    /// </summary>
    /// <remarks>
    /// Motion paths live in their own service and nothing connected its changes to the dirty flag,
    /// so an afternoon spent on keyframes and nothing else left the project looking saved: no
    /// prompt on the way out, and nothing for an autosave to write (2026-09-05 audit,
    /// persistence-11).
    /// </remarks>
    [Fact]
    public void Animating_a_layer_counts_too()
    {
        var (store, _, motion) = Create();
        var layer = Guid.NewGuid();

        motion.UpsertKeyframe(layer, nameof(CalloutClip), new MotionKeyframe { Time = 1.0, X = 0.4 });

        Assert.True(store.IsDirty);
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new NotSupportedException();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
