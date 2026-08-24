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
    bool ReportedByCurrentUser);

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
public sealed record CreateFeedPostRequest(string Body, Guid? ParentMessageId = null);

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
