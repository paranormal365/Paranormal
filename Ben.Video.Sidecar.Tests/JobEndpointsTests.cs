using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Item #38 phase 123 (F) — the segment-render job endpoints. Runs against the real ASP.NET
/// pipeline with the fake ffmpeg binary standing in for the real one (see
/// <c>SidecarWebApplicationFactory</c>): a full <c>PUT</c> source → <c>POST</c> job → poll status
/// → <c>GET</c> result → <c>DELETE</c> round trip actually executes a (fake) encode, proving the
/// whole pipeline — JSON deserialization, <c>SpecValidator</c>, <c>ArgvFactory</c>, the real
/// <c>FfmpegRunner</c> child-process path — works end to end, not just each piece in isolation.
/// </summary>
public sealed class JobEndpointsTests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public JobEndpointsTests(SidecarWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient(token: factory.ReadGeneratedPairingToken());
    }

    private static SegmentRenderSpec ValidVideoSpec(Guid clipId) => new(
        Kind: SegmentKind.Video,
        ClipId: clipId,
        SourceExt: SidecarWebApplicationFactory.ValidClipExt,
        Pass: RenderPassKind.Rough,
        Duration: 4.0,
        StartTrim: 0.0,
        EndTrim: 2.0,
        Speed: 1.0,
        MuteAudio: false,
        Gain: 1.0,
        OutputWidth: 320,
        OutputHeight: 180,
        Effects: null,
        AppliedEffects: [],
        VolumeAutomation: []);

    private async Task<Guid> UploadSourceAsync(Guid clipId)
    {
        var bytes = Encoding.UTF8.GetBytes("fake source bytes for job endpoint tests");
        var response = await _client.PutAsync(
            $"/v1/sources/{clipId:N}?ext={SidecarWebApplicationFactory.ValidClipExt.TrimStart('.')}",
            new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return clipId;
    }

    private async Task<JobStatusInfo> PollUntilTerminalAsync(Guid jobId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var response = await _client.GetAsync($"/v1/jobs/{jobId:N}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var info = await response.Content.ReadFromJsonAsync<JobStatusInfo>(SidecarJsonOptions.Default);
            Assert.NotNull(info);
            if (info!.State != JobState.Running) return info;
            await Task.Delay(50);
        }
        throw new TimeoutException("Job did not reach a terminal state in time.");
    }

    // ── Auth applies to job endpoints too ────────────────────────────────────

    [Fact]
    public async Task PostSegment_MissingToken_Returns401()
    {
        using var client = _factory.CreateAuthenticatedClient(token: null);
        var response = await client.PostAsJsonAsync(
            "/v1/jobs/segment", ValidVideoSpec(Guid.NewGuid()), SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Malformed/hostile bodies never reach ArgvFactory ─────────────────────

    [Fact]
    public async Task PostSegment_MalformedJson_Returns400()
    {
        using var content = new StringContent("{ this is not json", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/jobs/segment", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_UnknownFieldInBody_Returns400()
    {
        // SidecarJsonOptions.Default sets UnmappedMemberHandling.Disallow — an extra field that
        // doesn't map to SegmentRenderSpec is a hard parse failure, not silently ignored.
        var json = JsonSerializer.Serialize(ValidVideoSpec(Guid.NewGuid()), SidecarJsonOptions.Default);
        using var doc = JsonDocument.Parse(json);
        var mutable = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            mutable[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        mutable["totallyUnexpectedField"] = "surprise";

        using var content = new StringContent(JsonSerializer.Serialize(mutable), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v1/jobs/segment", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_EmptyClipId_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/jobs/segment", ValidVideoSpec(Guid.Empty), SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_DisallowedExtension_Returns400()
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with { SourceExt = ".exe" };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(50.0)]
    [InlineData(-1.0)]
    public async Task PostSegment_SpeedOutOfRange_Returns400(double speed)
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with { Speed = speed };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(321)] // odd — yuv420p requires even dimensions
    [InlineData(1)]
    [InlineData(-4)]
    [InlineData(50_000)]
    public async Task PostSegment_OutputWidthOutOfRange_Returns400(int width)
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with { OutputWidth = width };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_OutputWidthZero_IsAccepted()
    {
        // 0 is a legitimate sentinel (item #38 phase 124): "skip scale/pad at trim time,"
        // matching ExportService.TrimSegmentsAsync's own BuildTrimArgs call for video clips.
        var spec = ValidVideoSpec(Guid.NewGuid()) with { OutputWidth = 0, OutputHeight = 0 };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // ── Item #38 phase 124: the Export pass carries its own explicit quality ────

    private static readonly ExportQualityDto ValidExportQuality = new(
        VideoCodec: ExportVideoCodec.H264, AudioCodec: ExportAudioCodec.Aac,
        Bitrate: 4000, UseCrf: true, Crf: 20,
        IncludeAudio: true, AudioBitrate: 192, Preset: ExportPresetKind.Medium, Fps: 30);

    [Fact]
    public async Task PostSegment_ExportPassWithoutExportQuality_Returns400()
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with { Pass = RenderPassKind.Export, ExportQuality = null };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_NonExportPassWithExportQuality_Returns400()
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with { Pass = RenderPassKind.Fine, ExportQuality = ValidExportQuality };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(500_001)]
    public async Task PostSegment_ExportQualityBitrateOutOfRange_Returns400(int bitrate)
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with
        {
            Pass = RenderPassKind.Export,
            ExportQuality = ValidExportQuality with { Bitrate = bitrate },
        };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(64)]
    public async Task PostSegment_ExportQualityCrfOutOfRange_Returns400(int crf)
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with
        {
            Pass = RenderPassKind.Export,
            ExportQuality = ValidExportQuality with { Crf = crf },
        };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public async Task PostSegment_ExportQualityFpsOutOfRange_Returns400(int fps)
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with
        {
            Pass = RenderPassKind.Export,
            ExportQuality = ValidExportQuality with { Fps = fps },
        };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_ValidExportPass_Returns202()
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with { Pass = RenderPassKind.Export, ExportQuality = ValidExportQuality };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_UnknownEffectId_Returns400()
    {
        var spec = ValidVideoSpec(Guid.NewGuid()) with
        {
            AppliedEffects = [new AppliedEffectDto("definitely_not_a_real_effect", new Dictionary<string, double>())],
        };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_EffectParameterOutOfDeclaredRange_Returns400()
    {
        // color_grading's Brightness parameter is declared range [-1, 1] — 999 is nowhere near it.
        var spec = ValidVideoSpec(Guid.NewGuid()) with
        {
            AppliedEffects = [new AppliedEffectDto("color_grading", new Dictionary<string, double> { ["brightness"] = 999 })],
        };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostSegment_TooManyVolumeKeyframes_Returns400()
    {
        var keyframes = Enumerable.Range(0, 1001)
            .Select(i => new VolumeKeyframeDto(i / 1001.0, 1.0))
            .ToList();
        var spec = ValidVideoSpec(Guid.NewGuid()) with { VolumeAutomation = keyframes };
        var response = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Unknown job id ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatus_UnknownJobId_Returns404()
    {
        var response = await _client.GetAsync($"/v1/jobs/{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetResult_UnknownJobId_Returns404()
    {
        var response = await _client.GetAsync($"/v1/jobs/{Guid.NewGuid():N}/result");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Source not uploaded — the job itself fails, not the submission ──────

    [Fact]
    public async Task SourceNeverUploaded_JobFailsWithClearError()
    {
        var spec = ValidVideoSpec(Guid.NewGuid());
        var postResponse = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);
        var accepted = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetGuid();

        var final = await PollUntilTerminalAsync(jobId);
        Assert.Equal(JobState.Failed, final.State);
        Assert.Contains("uploaded", final.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Full happy-path round trip against the fake binary ──────────────────

    [Fact]
    public async Task FullRoundTrip_UploadSubmitPollResultDelete_Succeeds()
    {
        var clipId = Guid.NewGuid();
        await UploadSourceAsync(clipId);

        var spec = ValidVideoSpec(clipId);
        var postResponse = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);
        var accepted = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetGuid();

        var final = await PollUntilTerminalAsync(jobId);
        Assert.Equal(JobState.Succeeded, final.State);
        Assert.Equal(100, final.ProgressPercent);
        Assert.True(final.ResultSizeBytes > 0);

        var resultResponse = await _client.GetAsync($"/v1/jobs/{jobId:N}/result");
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        var bytes = await resultResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal("fake-encoded-bytes", Encoding.UTF8.GetString(bytes));

        var deleteResponse = await _client.DeleteAsync($"/v1/jobs/{jobId:N}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await _client.GetAsync($"/v1/jobs/{jobId:N}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task FullRoundTrip_ExportPass_Succeeds()
    {
        var clipId = Guid.NewGuid();
        await UploadSourceAsync(clipId);

        var spec = ValidVideoSpec(clipId) with { Pass = RenderPassKind.Export, ExportQuality = ValidExportQuality };
        var postResponse = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);
        var accepted = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetGuid();

        var final = await PollUntilTerminalAsync(jobId);
        Assert.Equal(JobState.Succeeded, final.State);
        Assert.True(final.ResultSizeBytes > 0);
    }

    [Fact]
    public async Task FullRoundTrip_ImageClip_Succeeds()
    {
        var clipId = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("fake png bytes");
        var putResponse = await _client.PutAsync($"/v1/sources/{clipId:N}?ext=png", new ByteArrayContent(bytes));
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var spec = new SegmentRenderSpec(
            Kind: SegmentKind.Image, ClipId: clipId, SourceExt: ".png", Pass: RenderPassKind.Fine,
            Duration: 5.0, StartTrim: 0, EndTrim: 0, Speed: 1, MuteAudio: false, Gain: 1,
            OutputWidth: 320, OutputHeight: 180, Effects: null, AppliedEffects: [], VolumeAutomation: []);

        var postResponse = await _client.PostAsJsonAsync("/v1/jobs/segment", spec, SidecarJsonOptions.Default);
        Assert.Equal(HttpStatusCode.Accepted, postResponse.StatusCode);
        var accepted = await postResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetGuid();

        var final = await PollUntilTerminalAsync(jobId);
        Assert.Equal(JobState.Succeeded, final.State);
    }
}
