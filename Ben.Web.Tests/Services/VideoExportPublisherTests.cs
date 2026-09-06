using Ben.Service.Models.Entities;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;
using Ben.Web.Services;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Which project a published render attaches itself to, and what happens when it cannot.
/// </summary>
/// <remarks>
/// <para>The transport half of this is covered by <see cref="VideoUploadRelayTests"/>. This is the
/// other half: the publish endpoint attaches a video to an <i>existing</i> project row and 404s
/// without one, so somebody who rendered without ever saving has nothing to publish against.</para>
///
/// <para>Its failure contract is unusual and worth pinning. Everything here throws, because the
/// editor's destination prompt catches, stays open and keeps "Save to my machine" available.
/// Returning normally tells the editor the video is safe, at which point it deletes the only
/// remaining copy — so a swallowed error loses somebody's render.</para>
/// </remarks>
public sealed class VideoExportPublisherTests
{
    private static readonly Guid Existing = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Created  = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Stale    = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ExportedVideo Render(Func<Task<byte[]?>>? readBytes = null) =>
        new("render.mp4", "video/mp4", 1024, 8.0,
            readBytes ?? (() => Task.FromResult<byte[]?>([1, 2, 3])));

    // ── Which project it attaches to ──────────────────────────────────────────

    /// <summary>
    /// A render made without ever saving creates the project first, then publishes to it.
    /// </summary>
    [Fact]
    public async Task A_project_that_was_never_saved_is_created_before_the_video_is_attached()
    {
        var (publisher, client, _) = Create();

        var result = await publisher.PublishAsync(Render(), caseId: null, knownProjectId: null);

        client.Verify(c => c.SaveMyVideoProjectAsync(
            It.IsAny<ProjectFile>(), null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(Created, result.ProjectId);
    }

    [Fact]
    public async Task A_case_project_is_created_against_its_case()
    {
        var caseId = Guid.NewGuid();
        var (publisher, client, _) = Create();

        await publisher.PublishAsync(Render(), caseId, knownProjectId: null);

        client.Verify(c => c.SaveMyVideoProjectAsync(
            It.IsAny<ProjectFile>(), caseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A project already on the server is published to, not duplicated.
    /// </summary>
    [Fact]
    public async Task A_project_already_on_the_server_is_not_created_again()
    {
        var (publisher, client, store) = Create();
        store.CurrentServerId = Existing;

        var result = await publisher.PublishAsync(Render(), caseId: null, knownProjectId: null);

        client.Verify(c => c.SaveMyVideoProjectAsync(
            It.IsAny<ProjectFile>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(Existing, result.ProjectId);
    }

    /// <summary>
    /// The project actually open wins over an id left behind by an earlier publish.
    /// </summary>
    /// <remarks>
    /// It used to be the other way round, so a stale id from an earlier publish outranked the
    /// project on screen and a later export attached itself to whichever project had been
    /// published first that session (2026-09-05 audit, site-4).
    /// </remarks>
    [Fact]
    public async Task The_open_project_outranks_a_stale_id_from_an_earlier_publish()
    {
        var (publisher, _, store) = Create();
        store.CurrentServerId = Existing;

        var result = await publisher.PublishAsync(Render(), caseId: null, knownProjectId: Stale);

        Assert.Equal(Existing, result.ProjectId);
    }

    [Fact]
    public async Task Without_an_open_project_the_callers_own_id_is_used()
    {
        var (publisher, client, _) = Create();

        var result = await publisher.PublishAsync(Render(), caseId: null, knownProjectId: Stale);

        Assert.Equal(Stale, result.ProjectId);
        client.Verify(c => c.SaveMyVideoProjectAsync(
            It.IsAny<ProjectFile>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The id is remembered, so a second export in the same session updates the same project
    /// rather than piling up a row per render.
    /// </summary>
    [Fact]
    public async Task Publishing_remembers_which_project_it_used()
    {
        var (publisher, _, store) = Create();

        await publisher.PublishAsync(Render(), caseId: null, knownProjectId: null);

        Assert.Equal(Created, store.CurrentServerId);
    }

    // ── When it cannot ────────────────────────────────────────────────────────

    /// <summary>
    /// A project that could not be saved has nowhere to attach a video, and says so.
    /// </summary>
    [Fact]
    public async Task A_project_that_could_not_be_saved_throws_rather_than_reporting_success()
    {
        var (publisher, client, _) = Create();
        client.Setup(c => c.SaveMyVideoProjectAsync(
                  It.IsAny<ProjectFile>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((VideoProjectRecord?)null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(Render(), caseId: null, knownProjectId: null));

        Assert.Contains("nowhere to attach", thrown.Message);
    }

    /// <summary>
    /// A render the browser will not hand back is not reported as published.
    /// </summary>
    /// <remarks>
    /// This is the case that would silently delete somebody's only copy.
    /// </remarks>
    [Fact]
    public async Task A_render_that_cannot_be_read_back_throws()
    {
        var (publisher, _, store) = Create();
        store.CurrentServerId = Existing;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(
                Render(() => Task.FromResult<byte[]?>(null)), caseId: null, knownProjectId: null));
    }

    [Fact]
    public async Task An_upload_the_server_rejects_throws()
    {
        var (publisher, client, store) = Create();
        store.CurrentServerId = Existing;

        client.Setup(c => c.PublishVideoProjectAsync(
                  It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync((VideoProjectRecord?)null);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(Render(), caseId: null, knownProjectId: null));

        Assert.Contains("rejected", thrown.Message);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private static (VideoExportPublisher Publisher, Mock<IBenAdminClient> Client, ProjectStore Store) Create()
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

        var client = new Mock<IBenAdminClient>();

        client.Setup(c => c.SaveMyVideoProjectAsync(
                  It.IsAny<ProjectFile>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new VideoProjectRecord { Id = Created, Name = "P", ProjectJson = "{}" });

        client.Setup(c => c.PublishVideoProjectAsync(
                  It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(new VideoProjectRecord { Id = Existing, Name = "P", ProjectJson = "{}" });

        // No relay: these tests are about which project, not about how the bytes travel.
        return (new VideoExportPublisher(client.Object, projSvc, store), client, store);
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
