using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Feed;

/// <summary>One post in the public feed, as a reader sees it.</summary>
/// <remarks>
/// <para><paramref name="Mentions"/> and <paramref name="Hashtags"/> come from the tables the
/// server filled when the post was written, not from re-reading the body. That is what makes a
/// link survive a rename: the mention carries the account's id, so the text can be linkified to
/// whoever it actually referred to rather than to whoever currently answers to that name.</para>
///
/// <para><paramref name="Body"/> is plain text. The feed is short-form and deliberately has no
/// rich-text editor — see the composer. It must be HTML-encoded at render.</para>
/// </remarks>
public sealed record FeedPostRecord(
    Guid Id,
    Guid AuthorAppUserId,
    string AuthorDisplayName,
    Guid? ParentMessageId,
    string Body,
    DateTime DateCreated,
    int ReplyCount,
    IReadOnlyList<FeedMentionRecord> Mentions,
    IReadOnlyList<string> Hashtags,
    bool AuthorIsFollowedByCurrentUser,
    bool IsOwnPost,
    /// <summary>True when the reader has already reported this post. Hides the report control.</summary>
    bool ReportedByCurrentUser,
    /// <summary>How many people liked this post (item 186 F3). Counted per page, never cached.</summary>
    int LikeCount = 0,
    /// <summary>Whether THIS reader liked it. Always false for a visitor.</summary>
    bool LikedByCurrentUser = false,
    /// <summary>
    /// Whether there is a photo or video to fetch from the post's own media route (item 186 F4).
    /// False while the media is unscreened — see <paramref name="MediaAwaitingReview"/>.
    /// </summary>
    bool HasMedia = false,
    /// <summary>
    /// Media was attached but has not been cleared yet, so nothing renders. Shown to the author
    /// as a note rather than as a broken image, which is the difference between "we are checking"
    /// and "your upload failed".
    /// </summary>
    bool MediaAwaitingReview = false,
    /// <summary>Photo or video, so the card knows which element to render.</summary>
    FeedMediaKind MediaKind = FeedMediaKind.None,
    /// <summary>What the author says this shows, from the experience taxonomy (item 186 F6).
    /// Null for uncategorized chatter.</summary>
    Guid? ExperienceTypeId = null,
    /// <summary>The type's display name, resolved server-side so a rename shows everywhere.</summary>
    string? ExperienceTypeName = null,
    /// <summary>
    /// AUTHOR-ONLY: the content doesn't look like its chosen type, so the card shows the
    /// recategorize nudge. Always false for every other reader — the signal exists to help the
    /// author, not to put an asterisk on them in public.
    /// </summary>
    bool CategoryMatchDegraded = false,
    /// <summary>
    /// The group whose case this footage came from — POPULATED ONLY WHEN CLAIMED (item 186 F7).
    /// Unclaimed and Declined emit nothing: absence is structural, so a client cannot forget to
    /// hide a link the group never agreed to.
    /// </summary>
    string? AttributedOrgName = null,
    string? AttributedOrgUrlName = null,
    /// <summary>The owning group vouches this footage is what it says (a claim, item 186 F7).</summary>
    bool GroupVerified = false,
    /// <summary>A person with the Moderator role cleared this post's media — distinct from the
    /// automatic screener's approval.</summary>
    bool ModeratorReviewed = false);

/// <summary>What kind of media a post carries.</summary>
public enum FeedMediaKind
{
    /// <summary>No media, or none the reader may see.</summary>
    None = 0,
    Image = 1,
    Video = 2,
}

/// <summary>
/// An account named in a post: the id it resolved to, the <c>@name</c> that was typed, and the
/// display name as it stands now.
/// </summary>
/// <remarks>
/// All three are needed. The <b>id</b> is what the link points at, so it survives a rename. The
/// <b>handle</b> is how the reader's text is matched back to this record, since that is what
/// appears in the body. The <b>display name</b> is what a reader would rather see in a tooltip or
/// a profile card than a handle.
/// </remarks>
public sealed record FeedMentionRecord(Guid AppUserId, string Handle, string DisplayName);

/// <summary>A page of the feed, with the cursor that continues it.</summary>
/// <remarks>
/// <paramref name="NextCursor"/> is null when there is nothing more. It is opaque: callers pass it
/// back unchanged and must not attempt to construct one, because its meaning is free to change.
/// </remarks>
/// <param name="CanPost">
/// Whether THIS reader may write in the feed (item 186 F2). Answered by the server, from the same
/// rule the create endpoint enforces, so the composer and the gate can never disagree — the shape
/// of bug where a UI cheerfully offers a box whose contents are then refused.
/// False for a visitor, and false for a signed-in person who belongs to no group and has no case.
/// </param>
public sealed record FeedPageRecord(
    IReadOnlyList<FeedPostRecord> Posts, string? NextCursor, bool CanPost = false);

/// <summary>What somebody's feed profile shows.</summary>
public sealed record FeedProfileRecord(
    Guid AppUserId,
    string DisplayName,
    int PostCount,
    int FollowerCount,
    int FollowingCount,
    bool IsFollowedByCurrentUser,
    bool IsSelf);

/// <summary>A new post.</summary>
/// <param name="Body">Plain text. Mentions and tags are parsed out of it by the server.</param>
/// <param name="ParentMessageId">The post being replied to, or null for a top-level post.</param>
/// <param name="ExperienceTypeId">What the post shows, from the experience taxonomy (item 186
/// F6). Optional always — encouraged for media, meaningless for chatter.</param>
/// <param name="SourceCaseId">The case this render came from (item 186 F7) — the editor's
/// "Post to the feed" sets it; a hand-written post never does. Requires media, and the author
/// must be able to see the case.</param>
/// <param name="ConsentToPublishPrivateEngagement">The explicit tick a PRIVATE-ENGAGEMENT
/// case's footage requires before it may go public. Ignored for ordinary cases; refusing to
/// tick it refuses the post.</param>
public sealed record CreateFeedPostRequest(
    string Body, Guid? ParentMessageId = null, Guid? ExperienceTypeId = null,
    Guid? SourceCaseId = null, bool ConsentToPublishPrivateEngagement = false);

// ── Org attribution (item 186 F7) ────────────────────────────────────────────

/// <summary>One case-derived post in a group's attribution queue.</summary>
/// <remarks>The media URL is the post's own public route, which only serves APPROVED media —
/// so a group admin reviewing a claim sees exactly what the public sees, no more.</remarks>
public sealed record FeedAttributionItem(
    Guid PostId,
    string Body,
    Guid AuthorAppUserId,
    string AuthorDisplayName,
    DateTime DateCreated,
    Guid? CaseId,
    string? CaseTitle,
    bool HasMedia,
    FeedMediaKind MediaKind,
    string? ExperienceTypeName,
    OrgAttributionState State,
    DateTime? DecidedUtc,
    string? DecidedByDisplayName);

/// <summary>The author's revised answer to "what does this show?" Null clears it.</summary>
public sealed record RecategorizeFeedPostRequest(Guid? ExperienceTypeId);

/// <summary>A report against a post.</summary>
public sealed record ReportFeedPostRequest(string? Reason);

/// <summary>One report in the moderation queue.</summary>
public sealed record FeedReportRecord(
    Guid Id,
    Guid OrgMessageId,
    string PostBody,
    Guid PostAuthorAppUserId,
    string PostAuthorDisplayName,
    DateTime PostDateCreated,
    bool PostIsHidden,
    Guid ReportedByAppUserId,
    string ReportedByDisplayName,
    string? Reason,
    FeedReportOutcome Outcome,
    DateTime DateCreated,
    DateTime? ResolvedUtc,
    string? ResolvedByDisplayName,
    /// <summary>How many people have reported this same post. Context an administrator wants.</summary>
    int ReportsAgainstThisPost);

/// <summary>An administrator's decision on a report.</summary>
/// <param name="Outcome">
/// Only <see cref="FeedReportOutcome.Dismissed"/> or <see cref="FeedReportOutcome.Hidden"/> — a
/// decision cannot be "Pending", which is the state a report starts in and is never returned to.
/// </param>
public sealed record ResolveFeedReportRequest(FeedReportOutcome Outcome);

/// <summary>
/// One post's media, as the review queue sees it (item 186 F5).
/// </summary>
/// <remarks>
/// Carries the post's text and the author's name because a moderator deciding about a photograph
/// needs the sentence it was posted with — the same picture reads differently under "the landing
/// at 3am" and under something else entirely.
/// </remarks>
/// <param name="MediaUrl">Where the moderator's browser fetches it. Served to moderators
/// regardless of review state; that is what reviewing means.</param>
public sealed record FeedMediaReviewItem(
    Guid PostId,
    Guid AuthorAppUserId,
    string AuthorDisplayName,
    string Body,
    DateTime DateCreated,
    FeedMediaReviewState State,
    string? Note,
    FeedMediaKind Kind,
    string MediaUrl,
    DateTime? ReviewedUtc,
    string? ReviewedByDisplayName,
    /// <summary>What the author says it shows (item 186 F6), for the category check beside the
    /// safety check. Null when uncategorized.</summary>
    Guid? ExperienceTypeId = null,
    string? ExperienceTypeName = null,
    /// <summary>How well the measured features fit that claim, 0–1. Context for the moderator,
    /// not a verdict.</summary>
    double? CategoryMatchScore = null,
    /// <summary>How many of this author's uploads the screener confidently refused in the last
    /// day, this one included (item 217). At three their uploads are paused; the badge says so.</summary>
    int AuthorRefusalsLast24h = 0);

/// <summary>A moderator's decision about one post's media.</summary>
/// <param name="Approve">True to publish it, false to hold it.</param>
/// <param name="Note">Optional note for the record. Never shown to the poster.</param>
public sealed record ReviewFeedMediaRequest(bool Approve, string? Note = null);

/// <summary>A moderator's judgment on a post's CATEGORY (item 186 F6) — separate from the
/// safety decision, because "safe to show" and "is what it says" are different questions.</summary>
/// <param name="Matches">True: the content is what its type says. False: it is not.</param>
public sealed record FeedCategoryVerdictRequest(bool Matches);

/// <summary>How much is waiting, for the queue's badge and the site-administration screens.</summary>
public sealed record FeedModerationSummary(
    int MediaAwaitingReview,
    int MediaHeld,
    int ReportsPending,
    bool ScreeningIsAutomatic,
    /// <summary>Whether the public feed feature is switched on (item 186 F10) — the fact the
    /// dark-launch reminder pivots on.</summary>
    bool FeedIsOn = false,
    /// <summary>How many feed posts exist, visible or not. Content accumulating while the
    /// feature is dark is the reminder's reason to exist.</summary>
    int FeedPostCount = 0);
