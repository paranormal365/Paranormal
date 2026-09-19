using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Web.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Ben.Service.Models.Entities;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Publishing a render from the site sends the file from the browser, not through the circuit.
/// </summary>
/// <remarks>
/// It used to read the whole render back into the circuit as one JS-interop byte[] return. Blazor
/// Server caps a JS-interop return value at 32 KB by default, and nothing raises it here — so a
/// real render, megabytes at the very least, could not be published from the site at all
/// (2026-09-05 audit, site-1).
/// </remarks>
public sealed class VideoUploadRelayTests
{
    private static ExportedVideo Render(string? blobUrl, Func<Task<byte[]?>>? readBytes = null) =>
        new("render.mp4", "video/mp4", 48_000_000, 29.5,
            readBytes ?? (() => Task.FromResult<byte[]?>([1, 2, 3])), blobUrl);

    [Fact]
    public async Task The_browser_uploads_the_file_when_a_relay_is_registered()
    {
        var relay = new RecordingRelay();
        var (publisher, _) = Create(relay);

        await publisher.PublishAsync(Render("blob:http://site/abc"), caseId: null, knownProjectId: KnownProject);

        Assert.Equal("blob:http://site/abc", relay.BlobUrl);
        Assert.Equal("render.mp4", relay.FileName);
    }

    /// <summary>
    /// The bytes must not be read at all on the relay path — reading them is the thing that
    /// cannot work.
    /// </summary>
    [Fact]
    public async Task The_render_is_never_pulled_through_the_circuit()
    {
        var read = false;
        var (publisher, _) = Create(new RecordingRelay());

        await publisher.PublishAsync(
            Render("blob:http://site/abc", () => { read = true; return Task.FromResult<byte[]?>([1]); }),
            caseId: null, knownProjectId: KnownProject);

        Assert.False(read);
    }

    /// <summary>
    /// A host with no relay still works. Correct for anything small, and the reason a missing
    /// registration degrades rather than breaks.
    /// </summary>
    [Fact]
    public async Task Without_a_relay_it_falls_back_to_the_bytes()
    {
        var read = false;
        var (publisher, spy) = Create(relay: null);

        await publisher.PublishAsync(
            Render(blobUrl: null, () => { read = true; return Task.FromResult<byte[]?>([1]); }),
            caseId: null, knownProjectId: KnownProject);

        Assert.True(read);
        Assert.True(spy.PublishedBytes);
    }

    /// <summary>
    /// A failed upload has to throw. The destination prompt catches it, stays open and keeps "Save
    /// to my machine" available; returning normally tells the editor the video is safe, at which
    /// point it discards the only remaining copy.
    /// </summary>
    [Fact]
    public async Task A_refused_upload_throws_rather_than_reporting_success()
    {
        var (publisher, _) = Create(new RecordingRelay { Problem = "The server said no." });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            publisher.PublishAsync(Render("blob:x"), caseId: null, knownProjectId: KnownProject));

        Assert.Contains("The server said no.", ex.Message);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private static readonly Guid KnownProject = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    private static (VideoExportPublisher Publisher, PublishSpy Spy) Create(IVideoUploadRelay? relay)
    {
        var opts    = Options.Create(new VideoEditorOptions());
        var clips   = new ClipStore(opts);
        var motion  = new MotionKeyframeService();
        var js      = new NoJs();
        var projSvc = new ProjectService(clips, motion, js, new NoHttp(), opts);
        var errors  = new ErrorLogService();
        var opfs    = new OPFSService(js, errors);
        var ffmpeg  = new FfmpegService(js, errors, new MemFsLedger(), new WorkerWatchdog());
        var store   = new ProjectStore(clips, projSvc, opfs, ffmpeg,
                                       new SourceMounter(ffmpeg, opfs), motion, js, errors);

        var spy    = new PublishSpy();
        var client = new Mock<IBenAdminClient>();

        client.Setup(c => c.GetMyVideoProjectAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new VideoProjectRecord { Id = KnownProject, Name = "Project", ProjectJson = "{}" });

        client.Setup(c => c.PublishVideoProjectAsync(
                  It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                  It.IsAny<CancellationToken>()))
              .Callback(() => spy.PublishedBytes = true)
              .ReturnsAsync(new VideoProjectRecord { Id = KnownProject, Name = "Project", ProjectJson = "{}" });

        return (new VideoExportPublisher(client.Object, projSvc, store, relay), spy);
    }

    /// <summary>Records whether the byte path was taken, which is the thing that cannot scale.</summary>
    private sealed class PublishSpy
    {
        public bool PublishedBytes { get; set; }
    }

    private sealed class RecordingRelay : IVideoUploadRelay
    {
        public string? Problem  { get; init; }
        public string? BlobUrl  { get; private set; }
        public string? FileName { get; private set; }

        public Task<string?> PublishAsync(
            Guid projectId, string blobUrl, string fileName, string contentType, CancellationToken ct = default)
        {
            BlobUrl  = blobUrl;
            FileName = fileName;
            return Task.FromResult(Problem);
        }
    }

    private sealed class NoJs : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
