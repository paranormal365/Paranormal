using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 159 — <c>POST /v1/probe</c>, <c>POST /v1/jobs/thumbnails</c>, and the multi-file
/// result endpoints, exercised through the real application pipeline.
///
/// <para>Uses its own factory with ffprobe present, since both features are gated on a verified
/// ffprobe being available (phase 158's capability model).</para>
/// </summary>
public sealed class ProbeAndThumbnailEndpointTests
    : IClassFixture<ProbeAndThumbnailEndpointTests.WithFfprobeFactory>
{
    public sealed class WithFfprobeFactory : SidecarWebApplicationFactory
    {
        public WithFfprobeFactory() => WithFfprobe = true;
    }

    private readonly WithFfprobeFactory _factory;
    private readonly HttpClient _client;

    public ProbeAndThumbnailEndpointTests(WithFfprobeFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(token: factory.ReadGeneratedPairingToken());
    }

    private async Task<Guid> UploadSourceAsync()
    {
        var clipId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("fake source bytes");
        var response = await _client.PutAsync(
            $"/v1/sources/{clipId:N}?ext={SidecarWebApplicationFactory.ValidClipExt.TrimStart('.')}",
            new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return clipId;
    }

    private async Task<JobStatusInfo> PollUntilTerminalAsync(Guid jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/v1/jobs/{jobId:N}");
            var info = await response.Content.ReadFromJsonAsync<JobStatusInfo>(SidecarJsonOptions.Default);
            Assert.NotNull(info);
            if (info!.State != JobState.Running) return info;
            await Task.Delay(50);
        }
        throw new TimeoutException("Job did not reach a terminal state in time.");
    }

    // ── Probe ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Probe_ReturnsTypedMetadata_NotRawFfprobeJson()
    {
        var clipId = await UploadSourceAsync();

        var response = await _client.PostAsJsonAsync(
            "/v1/probe",
            new MediaProbeRequest(clipId, SidecarWebApplicationFactory.ValidClipExt),
            SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<MediaProbeInfo>(SidecarJsonOptions.Default);

        // The fake ffprobe emits a realistic payload (string durations, numeric dimensions); what
        // matters is that the browser receives parsed fields, never ffprobe's own schema.
        Assert.NotNull(info);
        Assert.Equal(13.80, info!.Duration, 2);
        Assert.Equal(640, info.Width);
        Assert.Equal(360, info.Height);
    }

    [Fact]
    public async Task Probe_SourceNotUploaded_Returns404()
    {
        // The browser HEAD/PUTs first; a 404 here tells it to do that rather than implying the
        // file is broken.
        var response = await _client.PostAsJsonAsync(
            "/v1/probe",
            new MediaProbeRequest(Guid.NewGuid(), SidecarWebApplicationFactory.ValidClipExt),
            SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Probe_DisallowedExtension_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/probe", new MediaProbeRequest(Guid.NewGuid(), ".exe"), SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Thumbnails ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Thumbnails_ProduceManifestAndPerFileDownloads()
    {
        var clipId = await UploadSourceAsync();

        var submit = await _client.PostAsJsonAsync(
            "/v1/jobs/thumbnails",
            new ThumbnailJobRequest(clipId, SidecarWebApplicationFactory.ValidClipExt, Count: 3, Duration: 12.0),
            SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollUntilTerminalAsync(jobId);
        Assert.Equal(JobState.Succeeded, final.State);

        // /result answers with a manifest for multi-file kinds, not a file.
        var manifestResponse = await _client.GetAsync($"/v1/jobs/{jobId:N}/result");
        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<ResultManifest>(SidecarJsonOptions.Default);

        Assert.NotNull(manifest);
        Assert.Equal(3, manifest!.Files.Count);
        Assert.Equal(["thumb_1.webp", "thumb_2.webp", "thumb_3.webp"], manifest.Files.Select(f => f.Name));
        Assert.All(manifest.Files, f => Assert.True(f.SizeBytes > 0));

        foreach (var file in manifest.Files)
        {
            var fileResponse = await _client.GetAsync($"/v1/jobs/{jobId:N}/result/{file.Name}");
            Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
            Assert.Equal("image/webp", fileResponse.Content.Headers.ContentType?.MediaType);
            Assert.NotEmpty(await fileResponse.Content.ReadAsByteArrayAsync());
        }
    }

    [Theory]
    [InlineData("../../../etc/passwd")]
    [InlineData("..%2F..%2Fsecret.txt")]
    [InlineData("output.mp4")]      // a real file name, but from a different job kind
    [InlineData("thumb_99.webp")]   // right shape, not in this job's manifest
    public async Task ResultFile_NameOutsideTheManifest_Is404(string name)
    {
        // The manifest is the authorization list: a name that isn't in it never reaches a
        // filesystem call at all, so traversal has nothing to traverse.
        var clipId = await UploadSourceAsync();
        var submit = await _client.PostAsJsonAsync(
            "/v1/jobs/thumbnails",
            new ThumbnailJobRequest(clipId, SidecarWebApplicationFactory.ValidClipExt, Count: 2, Duration: 8.0),
            SidecarJsonOptions.Default);
        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        await PollUntilTerminalAsync(jobId);

        var response = await _client.GetAsync($"/v1/jobs/{jobId:N}/result/{name}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public async Task Thumbnails_CountOutOfRange_Returns400(int count)
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/jobs/thumbnails",
            new ThumbnailJobRequest(Guid.NewGuid(), SidecarWebApplicationFactory.ValidClipExt, count, Duration: 10),
            SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Thumbnails_SourceNotUploaded_JobFails()
    {
        // Validation passes (the request is well-formed) — the missing source is only discovered
        // once the job runs, so this must surface as a failed job rather than a 400.
        var submit = await _client.PostAsJsonAsync(
            "/v1/jobs/thumbnails",
            new ThumbnailJobRequest(Guid.NewGuid(), SidecarWebApplicationFactory.ValidClipExt, Count: 2, Duration: 8.0),
            SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollUntilTerminalAsync(jobId);

        Assert.Equal(JobState.Failed, final.State);
        Assert.Contains("not uploaded", final.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Thumbnails_RequireAuthToken()
    {
        // Must go through the factory so the request actually reaches the in-memory TestServer;
        // a raw HttpClient would try a real socket. Origin is valid, token is absent.
        using var unauthenticated = _factory.CreateAuthenticatedClient(token: null);

        var response = await unauthenticated.PostAsJsonAsync(
            "/v1/jobs/thumbnails",
            new ThumbnailJobRequest(Guid.NewGuid(), SidecarWebApplicationFactory.ValidClipExt, 2, 8.0),
            SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record JobAccepted(Guid JobId);
}
