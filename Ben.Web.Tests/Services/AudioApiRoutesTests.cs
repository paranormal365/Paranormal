using System.Net;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Ben.Web.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Every audio call goes to the address it means to, with the verb it means to.
/// </summary>
/// <remarks>
/// <para>A route with a typo in it is a 404, and every one of these methods turns a 404 into
/// <c>null</c> or an empty list — which the pages then render as "no markers", "no notes", "no
/// saved clips". Nothing throws and nothing is logged. That is a whole feature quietly reporting
/// an empty recording, and the only way to catch it before somebody notices is to check the
/// addresses (2026-09-06 audio audit, phase 6).</para>
///
/// <para>Twenty-two calls, none of which had a test of any kind.</para>
/// </remarks>
public sealed class AudioApiRoutesTests
{
    private const string BaseUrl = "http://localhost:5252";

    /// <summary>Records what was asked for and answers with whatever the test needs.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Json { get; set; } = "null";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Json, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (WebApiClient Client, CapturingHandler Handler) Build(string json = "null")
    {
        var handler = new CapturingHandler { Json = json };
        var http    = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return (new WebApiClient(http, new WebApiTokenStore { AccessToken = "t" }), handler);
    }

    private static readonly Guid File   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Marker = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Note   = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TypeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static string Path(CapturingHandler h) => h.LastRequest!.RequestUri!.PathAndQuery;
    private static HttpMethod Verb(CapturingHandler h) => h.LastRequest!.Method;

    // ── The player's saved configuration ──────────────────────────────────────

    [Fact]
    public async Task Reading_the_audio_config()
    {
        var (client, handler) = Build();
        await client.GetAudioConfigAsync(File);

        Assert.Equal($"/api/upload-files/{File}/audio-config", Path(handler));
        Assert.Equal(HttpMethod.Get, Verb(handler));
    }

    [Fact]
    public async Task Saving_the_audio_config()
    {
        var (client, handler) = Build();
        await client.UpsertAudioConfigAsync(File, new UpsertAudioConfigRequest { EnableSpectrogram = true });

        Assert.Equal($"/api/upload-files/{File}/audio-config", Path(handler));
        Assert.Equal(HttpMethod.Put, Verb(handler));
        Assert.Contains("\"enableSpectrogram\":true", handler.LastBody);
    }

    [Fact]
    public async Task Clearing_the_audio_config()
    {
        var (client, handler) = Build();
        await client.DeleteAudioConfigAsync(File);

        Assert.Equal($"/api/upload-files/{File}/audio-config", Path(handler));
        Assert.Equal(HttpMethod.Delete, Verb(handler));
    }

    // ── Region notes ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Reading_the_region_notes()
    {
        var (client, handler) = Build("[]");
        await client.GetRegionNotesAsync(File);

        Assert.Equal($"/api/upload-files/{File}/region-notes", Path(handler));
        Assert.Equal(HttpMethod.Get, Verb(handler));
    }

    [Fact]
    public async Task Writing_a_region_note()
    {
        var (client, handler) = Build();
        await client.CreateRegionNoteAsync(File,
            new CreateRegionNoteRequest(10, 20, "a label", null, "<p>heard a name</p>", false));

        Assert.Equal($"/api/upload-files/{File}/region-notes", Path(handler));
        Assert.Equal(HttpMethod.Post, Verb(handler));
        Assert.Contains("heard a name", handler.LastBody);
    }

    [Fact]
    public async Task Changing_a_region_note()
    {
        var (client, handler) = Build();
        await client.UpdateRegionNoteAsync(File, Note, new UpdateRegionNoteRequest(null, "<p>x</p>", false));

        Assert.Equal($"/api/upload-files/{File}/region-notes/{Note}", Path(handler));
        Assert.Equal(HttpMethod.Put, Verb(handler));
    }

    [Fact]
    public async Task Removing_a_region_note()
    {
        var (client, handler) = Build();
        await client.DeleteRegionNoteAsync(File, Note);

        Assert.Equal($"/api/upload-files/{File}/region-notes/{Note}", Path(handler));
        Assert.Equal(HttpMethod.Delete, Verb(handler));
    }

    // ── Markers ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reading_the_markers()
    {
        var (client, handler) = Build("[]");
        await client.GetAudioMarkersAsync(File);

        Assert.Equal($"/api/upload-files/{File}/audio-markers", Path(handler));
        Assert.Equal(HttpMethod.Get, Verb(handler));
    }

    [Fact]
    public async Task Placing_a_marker()
    {
        var (client, handler) = Build();
        await client.CreateAudioMarkerAsync(File,
            new CreateAudioMarkerRequest(12.5, "Says my name", EvpConfidenceLevel.Possible, null));

        Assert.Equal($"/api/upload-files/{File}/audio-markers", Path(handler));
        Assert.Equal(HttpMethod.Post, Verb(handler));
        Assert.Contains("Says my name", handler.LastBody);
    }

    [Fact]
    public async Task Changing_a_marker()
    {
        var (client, handler) = Build();
        await client.UpdateAudioMarkerAsync(File, Marker,
            new UpdateAudioMarkerRequest(1, "x", EvpConfidenceLevel.Possible, null));

        Assert.Equal($"/api/upload-files/{File}/audio-markers/{Marker}", Path(handler));
        Assert.Equal(HttpMethod.Put, Verb(handler));
    }

    [Fact]
    public async Task Removing_a_marker()
    {
        var (client, handler) = Build();
        await client.DeleteAudioMarkerAsync(File, Marker);

        Assert.Equal($"/api/upload-files/{File}/audio-markers/{Marker}", Path(handler));
        Assert.Equal(HttpMethod.Delete, Verb(handler));
    }

    [Fact]
    public async Task Reviewing_a_marker_has_its_own_address()
    {
        var (client, handler) = Build();
        await client.ReviewAudioMarkerAsync(File, Marker,
            new ReviewAudioMarkerRequest(EvpReviewStatus.Confirmed, "A voice", EvpConfidenceLevel.Possible));

        Assert.Equal($"/api/upload-files/{File}/audio-markers/{Marker}/review", Path(handler));
        Assert.Equal(HttpMethod.Put, Verb(handler));
    }

    [Fact]
    public async Task Replacing_the_candidates()
    {
        var (client, handler) = Build("[]");
        await client.ReplaceAudioCandidatesAsync(File,
            new BulkCreateAudioCandidatesRequest([new AudioCandidateRequest(1, 2, 50)]));

        Assert.Equal($"/api/upload-files/{File}/audio-markers/candidates", Path(handler));
        Assert.Equal(HttpMethod.Post, Verb(handler));
    }

    // ── Scanning ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EvpSensitivity.Low)]
    [InlineData(EvpSensitivity.Medium)]
    [InlineData(EvpSensitivity.High)]
    public async Task Scanning_carries_the_sensitivity_in_the_query(EvpSensitivity sensitivity)
    {
        var (client, handler) = Build("[]");
        await client.ScanAudioForEvpAsync(File, sensitivity);

        Assert.Equal($"/api/upload-files/{File}/audio-markers/scan?sensitivity={sensitivity}", Path(handler));
        Assert.Equal(HttpMethod.Post, Verb(handler));
    }

    /// <summary>
    /// "The scan found nothing" and "the scan did not happen" are different answers.
    /// </summary>
    /// <remarks>
    /// The first is a finding somebody may act on — it is most of what the feature is for. Handing
    /// back an empty list for a refused or failed request reports a clean recording that was never
    /// examined, which on this site is the worst kind of wrong.
    /// </remarks>
    [Fact]
    public async Task A_scan_that_found_nothing_is_an_empty_list()
    {
        var (client, handler) = Build("[]");
        handler.Status = HttpStatusCode.OK;

        var result = await client.ScanAudioForEvpAsync(File, EvpSensitivity.Medium);

        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_scan_that_did_not_happen_is_null(HttpStatusCode status)
    {
        var (client, handler) = Build("[]");
        handler.Status = status;

        Assert.Null(await client.ScanAudioForEvpAsync(File, EvpSensitivity.Medium));
    }

    [Fact]
    public async Task A_candidate_replace_that_did_not_happen_is_null()
    {
        var (client, handler) = Build("[]");
        handler.Status = HttpStatusCode.Forbidden;

        Assert.Null(await client.ReplaceAudioCandidatesAsync(File,
            new BulkCreateAudioCandidatesRequest([])));
    }

    // ── Clips and edits ───────────────────────────────────────────────────────

    [Fact]
    public async Task Saving_a_clip()
    {
        var (client, handler) = Build();
        await client.ClipAudioAsync(File, new ClipAudioRequest(1, 2, "name", false, TypeId));

        Assert.Equal($"/api/upload-files/{File}/clip", Path(handler));
        Assert.Equal(HttpMethod.Post, Verb(handler));
    }

    /// <summary>
    /// The preview's times are formatted invariantly, so a machine set to a comma decimal
    /// separator does not send <c>start=1,5</c> and get a 400 nobody can explain.
    /// </summary>
    [Fact]
    public async Task Previewing_a_clip_sends_its_times_in_the_invariant_form()
    {
        var (client, handler) = Build();
        await client.GetClipPreviewAsync(File, 1.5, 92.25);

        Assert.Equal($"/api/upload-files/{File}/clip/preview?start=1.5&end=92.25", Path(handler));
        Assert.Equal(HttpMethod.Get, Verb(handler));
    }

    [Fact]
    public async Task Listing_the_saved_clips()
    {
        var (client, handler) = Build("[]");
        await client.GetChildClipsAsync(File);

        Assert.Equal($"/api/upload-files/{File}/clips", Path(handler));
        Assert.Equal(HttpMethod.Get, Verb(handler));
    }

    [Fact]
    public async Task Applying_an_edit()
    {
        var (client, handler) = Build();
        await client.EditAudioAsync(File,
            new AudioEditRequest(AudioEditOperation.Normalize, null, null, null, null, null, null, false, TypeId));

        Assert.Equal($"/api/upload-files/{File}/audio-edit", Path(handler));
        Assert.Equal(HttpMethod.Post, Verb(handler));
    }

    /// <summary>
    /// The operation crosses the wire as its NAME, which is what a person or a script writes.
    /// </summary>
    /// <remarks>
    /// It used to bind only from a number, and <c>{"operation":"Normalize"}</c> came back as "the
    /// request field is required" — a rejected value reported as a missing one (2026-09-06 audio
    /// walk, finding R).
    /// </remarks>
    [Fact]
    public async Task An_edit_names_its_operation_on_the_wire()
    {
        var (client, handler) = Build();
        await client.EditAudioAsync(File,
            new AudioEditRequest(AudioEditOperation.Silence, 1, 2, null, null, null, null, false, TypeId));

        Assert.Contains("\"operation\":\"Silence\"", handler.LastBody);
    }

    // ── The reason-carrying variants ──────────────────────────────────────────

    /// <summary>
    /// A refusal written as a sentence reaches the caller; a framework blob does not.
    /// </summary>
    /// <remarks>
    /// Showing a person a <c>ProblemDetails</c> object or an HTML error page is worse than saying
    /// nothing useful, so anything that does not look like prose is dropped — which is why a 400
    /// from model binding once surfaced in the editor as "these settings aren't yours to change".
    /// </remarks>
    [Fact]
    public async Task A_refused_edit_carries_the_servers_sentence()
    {
        var (client, handler) = Build();
        handler.Status = HttpStatusCode.BadRequest;
        handler.Json   = "That recording is private, so an edit of it cannot be made public here.";

        var (result, error) = await client.EditAudioWithReasonAsync(File,
            new AudioEditRequest(AudioEditOperation.Normalize, null, null, null, null, null, null, true, TypeId));

        Assert.Null(result);
        Assert.Equal("That recording is private, so an edit of it cannot be made public here.", error);
    }

    [Fact]
    public async Task A_framework_error_is_not_shown_to_anybody()
    {
        var (client, handler) = Build();
        handler.Status = HttpStatusCode.BadRequest;
        handler.Json   = """{"title":"One or more validation errors occurred.","status":400}""";

        var (_, error) = await client.EditAudioWithReasonAsync(File,
            new AudioEditRequest(AudioEditOperation.Normalize, null, null, null, null, null, null, false, TypeId));

        Assert.Null(error);
    }

    [Fact]
    public async Task A_refused_clip_carries_the_servers_sentence()
    {
        var (client, handler) = Build();
        handler.Status = HttpStatusCode.BadRequest;
        handler.Json   = "That clip starts at 60s, and the recording is only 3s long.";

        var (result, error) = await client.ClipAudioWithReasonAsync(File,
            new ClipAudioRequest(60, 61, null, false, TypeId));

        Assert.Null(result);
        Assert.Contains("only 3s long", error);
    }

    [Fact]
    public async Task A_refused_config_save_carries_the_servers_sentence()
    {
        var (client, handler) = Build();
        handler.Status = HttpStatusCode.Forbidden;
        handler.Json   = "This recording isn't yours to change.";

        var (result, error) = await client.UpsertAudioConfigWithReasonAsync(
            File, new UpsertAudioConfigRequest());

        Assert.Null(result);
        Assert.Equal("This recording isn't yours to change.", error);
    }
}
