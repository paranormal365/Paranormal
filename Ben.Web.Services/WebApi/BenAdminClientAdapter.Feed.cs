using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Feed;
using Ben.Web.Services;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Feed half of the adapter — implements <see cref="Ben.Web.Services.IBenFeedClient"/>.
/// </summary>
/// <remarks>
/// Every read here degrades to empty rather than throwing. The API answers 404 for the whole
/// feature when the flag is off, and a component that renders nothing in that case is a better
/// outcome than one that throws inside a page the gate was about to hide anyway.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Reading ──────────────────────────────────────────────────────────────

    public Task<FeedPageRecord?> GetFeedAsync(
        string? mode = null, string? hashtag = null, string? cursor = null,
        CancellationToken token = default, Guid? author = null, Guid? experienceType = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(mode))    query.Add($"mode={Uri.EscapeDataString(mode)}");
        if (!string.IsNullOrWhiteSpace(hashtag)) query.Add($"hashtag={Uri.EscapeDataString(hashtag)}");
        if (!string.IsNullOrWhiteSpace(cursor))  query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (author is { } authorId)              query.Add($"author={authorId}");
        if (experienceType is { } typeId)        query.Add($"type={typeId}");

        var url = "/api/feed" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        return _api.GetAsync<FeedPageRecord>(url, token);
    }

    /// <summary>
    /// Where a post's photo or video is served from (item 186 F4).
    /// </summary>
    /// <remarks>
    /// Absolute, against the API host: the browser fetches this directly, and a relative path
    /// would ask the WEBSITE host for it — which serves static files and would answer 404. The
    /// same reason every other file URL on the site is built this way.
    /// </remarks>
    // ── Moderation (item 186 F5) ─────────────────────────────────────────────

    public Task<LoadResult<FeedMediaReviewItem>> GetFeedMediaReviewAsync(
        Ben.Data.Common.Enums.FeedMediaReviewState? state = null, CancellationToken token = default)
        => _api.GetListAsync<FeedMediaReviewItem>(
            "/api/moderation/feed-media" + (state is { } s ? $"?state={(int)s}" : string.Empty), token);

    public Task<bool> ReviewFeedMediaAsync(
        Guid postId, bool approve, string? note = null, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/moderation/feed-media/{postId}",
                              new ReviewFeedMediaRequest(approve, note), token);

    public Task<FeedModerationSummary?> GetModerationSummaryAsync(CancellationToken token = default)
        => _api.GetAsync<FeedModerationSummary>("/api/moderation/summary", token);

    public string GetModerationMediaUrl(Guid postId)
        => $"{_webApiBaseUrl}/api/moderation/feed-media/{postId}/file";

    public string GetFeedMediaUrl(Guid postId)
        => $"{_webApiBaseUrl}/api/feed/posts/{postId}/media";

    public Task<bool> LikeAsync(Guid postId, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/feed/posts/{postId}/like", new { }, token);

    public Task<bool> UnlikeAsync(Guid postId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/feed/posts/{postId}/like", token);

    public Task<LoadResult<FeedPostRecord>> GetThreadAsync(
        Guid postId, CancellationToken token = default)
            => _api.GetListAsync<FeedPostRecord>($"/api/feed/posts/{postId}", token);

    public Task<FeedProfileRecord?> GetFeedProfileAsync(Guid appUserId, CancellationToken token = default)
        => _api.GetAsync<FeedProfileRecord>($"/api/feed/profile/{appUserId}", token);

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a post, with or without a photo or video.
    /// </summary>
    /// <remarks>
    /// <para><b>Multipart either way</b> (item 186 F4). The endpoint takes a form so it can accept
    /// a file, and a form endpoint does not read JSON — so there is ONE request shape here rather
    /// than two paths that would drift, with the text-only case simply omitting the file part.</para>
    ///
    /// <para>The reason-carrying variant, because "a post can be at most 1000 characters" and
    /// "that file is neither a photo nor a video" are both things the composer shows against the
    /// box, and a plain null reads as "something broke".</para>
    /// </remarks>
    public async Task<(FeedPostRecord? Post, string? Error)> CreatePostAsync(
        string body, Guid? parentPostId = null, CancellationToken token = default,
        Stream? media = null, string? mediaFileName = null, string? mediaContentType = null,
        Guid? experienceTypeId = null,
        Guid? sourceCaseId = null, bool consentToPublishPrivateEngagement = false,
        NewPollRequest? poll = null,
        DateTime? scheduledForUtc = null,
        decimal? postedLatitude = null,
        decimal? postedLongitude = null,
        string? postedPlaceName = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(body), nameof(CreateFeedPostRequest.Body));
        if (parentPostId is { } parentId)
            form.Add(new StringContent(parentId.ToString()), nameof(CreateFeedPostRequest.ParentMessageId));
        if (experienceTypeId is { } typeId)
            form.Add(new StringContent(typeId.ToString()), nameof(CreateFeedPostRequest.ExperienceTypeId));
        if (sourceCaseId is { } caseId)
            form.Add(new StringContent(caseId.ToString()), nameof(CreateFeedPostRequest.SourceCaseId));
        if (consentToPublishPrivateEngagement)
            form.Add(new StringContent("true"), nameof(CreateFeedPostRequest.ConsentToPublishPrivateEngagement));

        // ── The composer's other tools (item 233) ────────────────────────────
        // The endpoint takes a form, so the poll goes over as the nested keys the model binder
        // reads — Poll.Question, Poll.Options[0] — rather than as a JSON blob the server would
        // have to parse a second way. One shape, one validator.
        if (poll is not null)
        {
            form.Add(new StringContent(poll.Question), "Poll.Question");
            for (var i = 0; i < poll.Options.Count; i++)
                form.Add(new StringContent(poll.Options[i]), $"Poll.Options[{i}]");
            form.Add(new StringContent(poll.AllowMultiple ? "true" : "false"), "Poll.AllowMultiple");
            if (poll.ClosesInHours is { } hours)
                form.Add(new StringContent(hours.ToString(CultureInfo.InvariantCulture)), "Poll.ClosesInHours");
        }

        // Round-trip format, so the instant survives the wire without a zone being guessed at
        // either end.
        if (scheduledForUtc is { } when)
            form.Add(new StringContent(when.ToString("O", CultureInfo.InvariantCulture)),
                nameof(CreateFeedPostRequest.ScheduledForUtc));

        // All three or none: a name with no point cannot be drawn, and a point with no name
        // reads as a pair of numbers.
        if (postedLatitude is { } lat && postedLongitude is { } lon)
        {
            form.Add(new StringContent(lat.ToString(CultureInfo.InvariantCulture)),
                nameof(CreateFeedPostRequest.PostedLatitude));
            form.Add(new StringContent(lon.ToString(CultureInfo.InvariantCulture)),
                nameof(CreateFeedPostRequest.PostedLongitude));
            if (!string.IsNullOrWhiteSpace(postedPlaceName))
                form.Add(new StringContent(postedPlaceName), nameof(CreateFeedPostRequest.PostedPlaceName));
        }

        StreamContent? mediaContent = null;
        if (media is not null && mediaFileName is not null)
        {
            mediaContent = new StreamContent(media);
            mediaContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(
                    mediaContentType ?? "application/octet-stream");
            form.Add(mediaContent, "media", mediaFileName);
        }

        try
        {
            return await _api.PostMultipartExpectingReasonAsync<FeedPostRecord>(
                "/api/feed/posts", form, token);
        }
        finally
        {
            mediaContent?.Dispose();
        }
    }

    public Task<(FeedPostRecord? Post, string? Error)> RecategorizeAsync(
        Guid postId, Guid? experienceTypeId, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<RecategorizeFeedPostRequest, FeedPostRecord>(
            HttpMethod.Put, $"/api/feed/posts/{postId}/experience-type",
            new RecategorizeFeedPostRequest(experienceTypeId), token);

    public Task<bool> JudgeFeedCategoryAsync(Guid postId, bool matches, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/moderation/feed-categories/{postId}",
                              new FeedCategoryVerdictRequest(matches), token);

    public Task<LoadResult<FeedAttributionItem>> GetFeedAttributionsAsync(
        Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<FeedAttributionItem>($"/api/organizations/{orgId}/feed-attributions", token);

    public Task<bool> ClaimFeedAttributionAsync(Guid orgId, Guid postId, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/organizations/{orgId}/feed-attributions/{postId}/claim", new { }, token);

    public Task<bool> DeclineFeedAttributionAsync(Guid orgId, Guid postId, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/organizations/{orgId}/feed-attributions/{postId}/decline", new { }, token);

    public Task<bool> ReportPostAsync(Guid postId, string? reason, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/feed/posts/{postId}/report", new ReportFeedPostRequest(reason), token);

    public Task<bool> FollowAsync(Guid appUserId, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/feed/follow/{appUserId}", new { }, token);

    public Task<bool> UnfollowAsync(Guid appUserId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/feed/follow/{appUserId}", token);

    // ── Moderation ───────────────────────────────────────────────────────────

    public Task<MessagePollRecord?> GetPollAsync(Guid pollId, CancellationToken token = default)
        => _api.GetAsync<MessagePollRecord>($"/api/polls/{pollId}", token);

    public Task<MessagePollRecord?> CastPollVoteAsync(
        Guid pollId, IReadOnlyList<Guid> optionIds, CancellationToken token = default)
        => _api.PostAsync<object, MessagePollRecord>(
               $"/api/polls/{pollId}/votes", new { OptionIds = optionIds }, token);

    public Task<LoadResult<GiphyItem>> SearchGifsAsync(string? term, CancellationToken token = default)
        => _api.GetListAsync<GiphyItem>(
               string.IsNullOrWhiteSpace(term)
                   ? "/api/giphy/search"
                   : $"/api/giphy/search?q={Uri.EscapeDataString(term.Trim())}",
               token);

    // ── A post still waiting for its hour (item 233) ─────────────────────────

    public Task<FeedPostRecord?> PublishScheduledNowAsync(Guid postId, CancellationToken token = default)
        => _api.PostAsync<object, FeedPostRecord>($"/api/feed/posts/{postId}/publish-now", new { }, token);

    public Task<bool> CancelScheduledPostAsync(Guid postId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/feed/posts/{postId}/schedule", token);

    public Task<LoadResult<FeedReportRecord>> GetFeedReportsAsync(
        FeedReportOutcome? outcome = null, CancellationToken token = default)
    {
        var url = "/api/admin/feed/reports"
                + (outcome is { } wanted ? $"?outcome={(int)wanted}" : string.Empty);

        return _api.GetListAsync<FeedReportRecord>(url, token);
    }

    public Task<bool> ResolveFeedReportAsync(
        Guid reportId, FeedReportOutcome outcome, CancellationToken token = default)
        => _api.PostVoidAsync(
            $"/api/admin/feed/reports/{reportId}/resolve", new ResolveFeedReportRequest(outcome), token);
}
