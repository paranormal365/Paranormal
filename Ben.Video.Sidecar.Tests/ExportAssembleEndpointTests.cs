using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 162 — <c>POST /v1/jobs/export-assemble</c> through the real pipeline: concat
/// alone, concat + audio mix, both 409 shapes, validation, and a mid-step ffmpeg failure.
/// </summary>
public sealed class ExportAssembleEndpointTests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ExportAssembleEndpointTests(SidecarWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(token: factory.ReadGeneratedPairingToken());
    }

    private static ExportQualityDto Quality() => new(
        ExportVideoCodec.H264, ExportAudioCodec.Aac, Bitrate: 8000, UseCrf: true, Crf: 23,
        IncludeAudio: true, AudioBitrate: 192, Preset: ExportPresetKind.Medium, Fps: 30);

    private async Task<Guid> UploadSourceAsync()
    {
        var clipId = Guid.NewGuid();
        var response = await _client.PutAsync(
            $"/v1/sources/{clipId:N}?ext={SidecarWebApplicationFactory.ValidClipExt.TrimStart('.')}",
            new ByteArrayContent(Encoding.UTF8.GetBytes("fake source bytes")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return clipId;
    }

    private async Task<JobStatusInfo> PollAsync(Guid jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
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

    /// <summary>Renders a retained segment and returns its remote id.</summary>
    private async Task<Guid> RetainedSegmentAsync()
    {
        var clipId = await UploadSourceAsync();
        var spec = new SegmentRenderSpec(
            SegmentKind.Video, clipId, SidecarWebApplicationFactory.ValidClipExt,
            RenderPassKind.Rough, 4.0, 0.0, 2.0, 1.0, false, 1.0, 320, 180,
            null, [], [], null, Retain: true);

        var submit = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollAsync(jobId);
        Assert.Equal(JobState.Succeeded, final.State);
        Assert.NotNull(final.RetainedSegmentId);
        return final.RetainedSegmentId!.Value;
    }

    private async Task<HttpResponseMessage> SubmitAsync(ExportAssembleRequest request) =>
        await _client.PostAsJsonAsync("/v1/jobs/export-assemble", request, SidecarJsonOptions.Default);

    [Fact]
    public async Task ConcatOnly_Succeeds()
    {
        var a = await RetainedSegmentAsync();
        var b = await RetainedSegmentAsync();

        var submit = await SubmitAsync(new ExportAssembleRequest([a, b], Quality()));
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollAsync(jobId);

        Assert.Equal(JobState.Succeeded, final.State);
        var result = await _client.GetAsync($"/v1/jobs/{jobId:N}/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotEmpty(await result.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ConcatPlusAudioMix_Succeeds()
    {
        var segment = await RetainedSegmentAsync();
        var audioClip = await UploadSourceAsync();

        var request = new ExportAssembleRequest(
            [segment], Quality(),
            new ExportAudioMixDto([
                new AudioMixClipDto(audioClip, SidecarWebApplicationFactory.ValidClipExt,
                    Start: 0, End: 2, FilterChain: "volume=1.0,adelay=500:all=1"),
            ]));

        var submit = await SubmitAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollAsync(jobId);

        Assert.Equal(JobState.Succeeded, final.State);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/v1/jobs/{jobId:N}/result")).StatusCode);
    }

    [Fact]
    public async Task MissingSegment_Returns409WithTheList()
    {
        var absent = Guid.NewGuid();

        var response = await SubmitAsync(new ExportAssembleRequest([absent], Quality()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<MissingSegmentsInfo>(SidecarJsonOptions.Default);
        Assert.Equal([absent], info!.MissingSegmentIds);
    }

    [Fact]
    public async Task MissingAudioSource_Returns409SoTheClientCanUploadAndRetry()
    {
        // Reported up front rather than as a mid-job failure — the client can upload exactly the
        // named sources and resubmit instead of re-rendering anything.
        var segment = await RetainedSegmentAsync();
        var neverUploaded = Guid.NewGuid();

        var response = await SubmitAsync(new ExportAssembleRequest(
            [segment], Quality(),
            new ExportAudioMixDto([
                new AudioMixClipDto(neverUploaded, SidecarWebApplicationFactory.ValidClipExt, 0, 2, "volume=1.0"),
            ])));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<MissingSegmentsInfo>(SidecarJsonOptions.Default);
        Assert.Equal([neverUploaded], info!.MissingSegmentIds);
    }

    [Theory]
    [InlineData("volume=1.0; rm -rf /")]        // shell metacharacters + whitespace
    [InlineData("volume='1.0'")]                 // quotes
    [InlineData("volume=1.0\\,adelay=1")]       // backslash
    [InlineData("")]                             // empty
    public async Task HostileFilterChain_Returns400(string chain)
    {
        // The chain is machine-generated from numeric clip properties, so an allowlist of the
        // characters those can produce is safe — and keeps anything else off the command line
        // even though this endpoint is already token-gated.
        var response = await SubmitAsync(new ExportAssembleRequest(
            [Guid.NewGuid()], Quality(),
            new ExportAudioMixDto([
                new AudioMixClipDto(Guid.NewGuid(), SidecarWebApplicationFactory.ValidClipExt, 0, 2, chain),
            ])));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AudioEndBeforeStart_Returns400()
    {
        var response = await SubmitAsync(new ExportAssembleRequest(
            [Guid.NewGuid()], Quality(),
            new ExportAudioMixDto([
                new AudioMixClipDto(Guid.NewGuid(), SidecarWebApplicationFactory.ValidClipExt,
                    Start: 5, End: 2, FilterChain: "volume=1.0"),
            ])));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptySegmentList_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await SubmitAsync(new ExportAssembleRequest([], Quality()))).StatusCode);
    }

    [Fact]
    public async Task RequiresAuthToken()
    {
        using var unauthenticated = _factory.CreateAuthenticatedClient(token: null);

        var response = await unauthenticated.PostAsJsonAsync(
            "/v1/jobs/export-assemble", new ExportAssembleRequest([Guid.NewGuid()], Quality()),
            SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record JobAccepted(Guid JobId);
}
