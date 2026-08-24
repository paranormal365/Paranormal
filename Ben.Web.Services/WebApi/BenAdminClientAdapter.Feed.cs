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

    public async Task<(FeedPostRecord? Post, string? Error)> CreatePostAsync(
        string body, Guid? parentPostId = null, CancellationToken token = default)
    {
        // SendExpectingReason rather than Post: "a post can be at most 1000 characters" is
        // something the composer can show against the box, and the plain helper would flatten it
        // into a null that reads as "something broke".
        var (post, error) = await _api.SendExpectingReasonAsync<CreateFeedPostRequest, FeedPostRecord>(
            HttpMethod.Post, "/api/feed/posts", new CreateFeedPostRequest(body, parentPostId), token);

        return (post, error);
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
