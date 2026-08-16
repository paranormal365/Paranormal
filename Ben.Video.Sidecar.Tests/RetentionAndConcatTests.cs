using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #70 phase 160 — segment retention (dual residency), <c>DELETE /v1/segments/{id}</c>, and
/// <c>POST /v1/jobs/concat</c>, through the real application pipeline.
/// </summary>
public sealed class RetentionAndConcatTests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RetentionAndConcatTests(SidecarWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(token: factory.ReadGeneratedPairingToken());
    }

    private static SegmentRenderSpec Spec(Guid clipId, bool retain) => new(
        Kind: SegmentKind.Video, ClipId: clipId,
        SourceExt: SidecarWebApplicationFactory.ValidClipExt,
        Pass: RenderPassKind.Rough, Duration: 4.0, StartTrim: 0.0, EndTrim: 2.0,
        Speed: 1.0, MuteAudio: false, Gain: 1.0, OutputWidth: 320, OutputHeight: 180,
        Effects: null, AppliedEffects: [], VolumeAutomation: [], ExportQuality: null,
        Retain: retain);

    private async Task<Guid> UploadSourceAsync()
    {
        var clipId = Guid.NewGuid();
        var response = await _client.PutAsync(
            $"/v1/sources/{clipId:N}?ext={SidecarWebApplicationFactory.ValidClipExt.TrimStart('.')}",
            new ByteArrayContent(Encoding.UTF8.GetBytes("fake source bytes")));
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

    /// <summary>Runs a segment job to completion and returns its status.</summary>
    private async Task<JobStatusInfo> RenderSegmentAsync(bool retain)
    {
        var clipId = await UploadSourceAsync();
        var submit = await _client.PostAsJsonAsync("/v1/jobs/segment", Spec(clipId, retain), SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollUntilTerminalAsync(jobId);
        Assert.Equal(JobState.Succeeded, final.State);
        return final;
    }

    // ── Retention ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retain_True_ReturnsRetainedSegmentId()
    {
        var final = await RenderSegmentAsync(retain: true);
        Assert.NotNull(final.RetainedSegmentId);
        Assert.NotEqual(Guid.Empty, final.RetainedSegmentId!.Value);
    }

    [Fact]
    public async Task Retain_False_LeavesNoRetainedId()
    {
        // The default path must be completely unchanged from phase 123 — no retained id, no
        // residue in the segment store.
        var final = await RenderSegmentAsync(retain: false);
        Assert.Null(final.RetainedSegmentId);
    }

    [Fact]
    public async Task Retain_True_ResultIsStillDownloadable()
    {
        // Dual residency's core promise: retention MOVES the file into the segment store, so the
        // browser's own /result download must still work afterwards. If ResultPath weren't
        // repointed this would 404 or serve a dangling path.
        var final = await RenderSegmentAsync(retain: true);

        var result = await _client.GetAsync($"/v1/jobs/{final.JobId:N}/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotEmpty(await result.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DeleteSegment_IsIdempotent()
    {
        var final = await RenderSegmentAsync(retain: true);
        var segmentId = final.RetainedSegmentId!.Value;

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/v1/segments/{segmentId:N}")).StatusCode);
        // Second delete, and a never-existed id, both 204 — a client tidying up after a sidecar
        // restart would otherwise see a flood of spurious 404s for work it rightly believes it
        // should be doing.
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/v1/segments/{segmentId:N}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/v1/segments/{Guid.NewGuid():N}")).StatusCode);
    }

    // ── Concat ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Concat_OverRetainedSegments_Succeeds()
    {
        var first = await RenderSegmentAsync(retain: true);
        var second = await RenderSegmentAsync(retain: true);

        var submit = await _client.PostAsJsonAsync(
            "/v1/jobs/concat",
            new ConcatJobRequest([first.RetainedSegmentId!.Value, second.RetainedSegmentId!.Value]),
            SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);

        var jobId = (await submit.Content.ReadFromJsonAsync<JobAccepted>(SidecarJsonOptions.Default))!.JobId;
        var final = await PollUntilTerminalAsync(jobId);

        Assert.Equal(JobState.Succeeded, final.State);
        var result = await _client.GetAsync($"/v1/jobs/{jobId:N}/result");
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.NotEmpty(await result.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Concat_MissingSegment_Returns409WithTheMissingList()
    {
        // The caller needs to know WHICH ids went away so it can re-render exactly those, rather
        // than guessing or redoing the whole timeline.
        var present = await RenderSegmentAsync(retain: true);
        var absentA = Guid.NewGuid();
        var absentB = Guid.NewGuid();

        var response = await _client.PostAsJsonAsync(
            "/v1/jobs/concat",
            new ConcatJobRequest([present.RetainedSegmentId!.Value, absentA, absentB]),
            SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<MissingSegmentsInfo>(SidecarJsonOptions.Default);
        Assert.NotNull(info);
        Assert.Equal([absentA, absentB], info!.MissingSegmentIds);
    }

    [Fact]
    public async Task Concat_AfterSegmentDeleted_Returns409()
    {
        var segment = await RenderSegmentAsync(retain: true);
        var segmentId = segment.RetainedSegmentId!.Value;
        await _client.DeleteAsync($"/v1/segments/{segmentId:N}");

        var response = await _client.PostAsJsonAsync(
            "/v1/jobs/concat", new ConcatJobRequest([segmentId]), SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Concat_EmptyList_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/jobs/concat", new ConcatJobRequest([]), SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Concat_EmptyGuid_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/jobs/concat", new ConcatJobRequest([Guid.Empty]), SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Concat_RequiresAuthToken()
    {
        // A separate client with a valid Origin and no token — never mutate the shared one, whose
        // headers other tests in this class depend on.
        using var unauthenticated = _factory.CreateAuthenticatedClient(token: null);

        var response = await unauthenticated.PostAsJsonAsync(
            "/v1/jobs/concat", new ConcatJobRequest([Guid.NewGuid()]), SidecarJsonOptions.Default);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record JobAccepted(Guid JobId);
}
