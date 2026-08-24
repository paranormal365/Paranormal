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
        CancellationToken token = default, Guid? author = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(mode))    query.Add($"mode={Uri.EscapeDataString(mode)}");
        if (!string.IsNullOrWhiteSpace(hashtag)) query.Add($"hashtag={Uri.EscapeDataString(hashtag)}");
        if (!string.IsNullOrWhiteSpace(cursor))  query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (author is { } authorId)              query.Add($"author={authorId}");

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
        Stream? media = null, string? mediaFileName = null, string? mediaContentType = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(body), nameof(CreateFeedPostRequest.Body));
        if (parentPostId is { } parentId)
            form.Add(new StringContent(parentId.ToString()), nameof(CreateFeedPostRequest.ParentMessageId));

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

    public Task<bool> ReportPostAsync(Guid postId, string? reason, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/feed/posts/{postId}/report", new ReportFeedPostRequest(reason), token);

    public Task<bool> FollowAsync(Guid appUserId, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/feed/follow/{appUserId}", new { }, token);

    public Task<bool> UnfollowAsync(Guid appUserId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/feed/follow/{appUserId}", token);

    // ── Moderation ───────────────────────────────────────────────────────────

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
