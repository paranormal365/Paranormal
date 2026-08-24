using Ben.Web.Services.WebApi;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Feed;

namespace Ben.Web.Services;

/// <summary>
/// The Feed slice of <see cref="IBenAdminClient"/> — the public feed, follows, and moderation.
/// </summary>
/// <remarks>
/// <para>Every method here returns an empty or null answer when the feed is switched off, because
/// the API 404s wholesale in that case. Callers should not need to check the flag before asking;
/// the pages that host these are behind a <c>FeatureGate</c> anyway, and a component that renders
/// nothing is a better failure than one that throws.</para>
///
/// <para>Paging is by opaque cursor. Pass <c>NextCursor</c> from the previous page back unchanged
/// and never attempt to construct one — its meaning is free to change.</para>
/// </remarks>
public interface IBenFeedClient
{
    /// <summary>
    /// A page of the feed, newest first.
    /// </summary>
    /// <param name="mode">
    /// <c>all</c> for everybody's posts, <c>following</c> for the people this person follows plus
    /// their own. Null reads as <c>all</c>.
    /// </param>
    /// <param name="hashtag">Narrow to one tag. Combines with <paramref name="mode"/>.</param>
    /// <param name="cursor">From a previous page. Opaque.</param>
    /// <param name="token">Cancellation.</param>
    /// <param name="author">One person's posts. Overrides <paramref name="mode"/>.</param>
    /// <param name="experienceType">One experience type's posts (item 186 F6). Combines like a tag.</param>
    Task<FeedPageRecord?> GetFeedAsync(
        string? mode = null, string? hashtag = null, string? cursor = null,
        CancellationToken token = default, Guid? author = null, Guid? experienceType = null);

    // ── Moderation (item 186 F5) ─────────────────────────────────────────────

    /// <summary>Feed media in one review state, oldest first. Moderators only.</summary>
    Task<LoadResult<FeedMediaReviewItem>> GetFeedMediaReviewAsync(
        Ben.Data.Common.Enums.FeedMediaReviewState? state = null, CancellationToken token = default);

    /// <summary>Approves or holds one post's media. Returns false when the call was refused.</summary>
    Task<bool> ReviewFeedMediaAsync(
        Guid postId, bool approve, string? note = null, CancellationToken token = default);

    /// <summary>How much is waiting, and whether screening is automatic.</summary>
    Task<FeedModerationSummary?> GetModerationSummaryAsync(CancellationToken token = default);

    /// <summary>Where a moderator's browser fetches a file under review, whatever its state.</summary>
    string GetModerationMediaUrl(Guid postId);

    /// <summary>Where a post's photo or video is served from. Absolute, against the API host.</summary>
    string GetFeedMediaUrl(Guid postId);

    /// <summary>Likes a post. Idempotent — liking twice is liking once.</summary>
    Task<bool> LikeAsync(Guid postId, CancellationToken token = default);

    /// <summary>Takes a like back. Forgiving — unliking what was never liked succeeds.</summary>
    Task<bool> UnlikeAsync(Guid postId, CancellationToken token = default);

    /// <summary>One post and its replies, the root first. Empty when the post is gone or hidden.</summary>
    Task<LoadResult<FeedPostRecord>> GetThreadAsync(Guid postId, CancellationToken token = default);

    /// <summary>Somebody's feed profile — their counts, and whether the reader follows them.</summary>
    Task<FeedProfileRecord?> GetFeedProfileAsync(Guid appUserId, CancellationToken token = default);

    /// <summary>
    /// Posts, or replies to one. Returns the post as it will be read, or the server's refusal.
    /// </summary>
    /// <remarks>
    /// The refusal is worth having rather than a bare null: "a post can be at most 1000 characters"
    /// is something a person can act on, and the composer shows it against the box.
    /// </remarks>
    /// <param name="media">Optional photo or video. Null for a text post.</param>
    /// <param name="mediaFileName">Its file name — required when <paramref name="media"/> is given.</param>
    /// <param name="mediaContentType">Its content type.</param>
    /// <param name="experienceTypeId">What the post shows, from the taxonomy (item 186 F6).</param>
    /// <param name="sourceCaseId">The case the render came from (item 186 F7) — set only by the
    /// editor's "Post to the feed"; requires media.</param>
    /// <param name="consentToPublishPrivateEngagement">The explicit tick a private-engagement
    /// case's footage requires. The server refuses without it.</param>
    Task<(FeedPostRecord? Post, string? Error)> CreatePostAsync(
        string body, Guid? parentPostId = null, CancellationToken token = default,
        Stream? media = null, string? mediaFileName = null, string? mediaContentType = null,
        Guid? experienceTypeId = null,
        Guid? sourceCaseId = null, bool consentToPublishPrivateEngagement = false);

    // ── Org attribution (item 186 F7) ────────────────────────────────────────

    /// <summary>A group's case-derived posts: undecided first. Group admins only.</summary>
    Task<LoadResult<FeedAttributionItem>> GetFeedAttributionsAsync(
        Guid orgId, CancellationToken token = default);

    /// <summary>Puts the group's name on the post — and vouches for the footage.</summary>
    Task<bool> ClaimFeedAttributionAsync(Guid orgId, Guid postId, CancellationToken token = default);

    /// <summary>Says no. The post stays; the group's name never appears on it.</summary>
    Task<bool> DeclineFeedAttributionAsync(Guid orgId, Guid postId, CancellationToken token = default);

    /// <summary>
    /// The author changes what a post claims to show (item 186 F6) — the nudge's one-click
    /// answer. Null clears the category. Returns the re-read post, or the refusal.
    /// </summary>
    Task<(FeedPostRecord? Post, string? Error)> RecategorizeAsync(
        Guid postId, Guid? experienceTypeId, CancellationToken token = default);

    /// <summary>
    /// A moderator's category judgment (item 186 F6): the content matches its type, or it
    /// doesn't. Writes a labelled example; touches nothing on the post itself.
    /// </summary>
    Task<bool> JudgeFeedCategoryAsync(Guid postId, bool matches, CancellationToken token = default);

    /// <summary>Reports a post. Idempotent — reporting twice is one report.</summary>
    Task<bool> ReportPostAsync(Guid postId, string? reason, CancellationToken token = default);

    Task<bool> FollowAsync(Guid appUserId, CancellationToken token = default);
    Task<bool> UnfollowAsync(Guid appUserId, CancellationToken token = default);

    // ── Moderation (SuperAdmin) ──────────────────────────────────────────────

    /// <summary>The moderation queue, oldest first. Omit the outcome for what is still pending.</summary>
    Task<LoadResult<FeedReportRecord>> GetFeedReportsAsync(
        FeedReportOutcome? outcome = null, CancellationToken token = default);

    /// <summary>
    /// Decides a report: <see cref="FeedReportOutcome.Dismissed"/> or
    /// <see cref="FeedReportOutcome.Hidden"/>. Resolves every pending report against the same post
    /// together, because five people reporting one post is one decision.
    /// </summary>
    Task<bool> ResolveFeedReportAsync(
        Guid reportId, FeedReportOutcome outcome, CancellationToken token = default);
}
