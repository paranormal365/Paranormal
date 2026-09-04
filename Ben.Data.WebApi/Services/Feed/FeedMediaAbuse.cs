using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Feed;

/// <summary>
/// The one case where the feed refuses an upload outright instead of queuing it for a person
/// (item 217): an account that keeps sending what the screener is confident is pornography.
/// </summary>
/// <remarks>
/// <para>Ben's rule, 2026-09-04: a refused upload should go to a moderator for approval rather
/// than be denied — "unless the person is spamming it". Everything the screener holds still goes
/// to the Held pile where a moderator can approve it. This class only decides when an account
/// has used that up.</para>
///
/// <para><b>Only confident refusals count.</b> A post counts when the screener scored it at or
/// above <see cref="NsfwDecision.BlockThreshold"/> and it is still Held. The borderline band
/// never counts, so a run of dark, skin-toned frames from a real investigation cannot pause
/// anybody — a false positive costs a moderator's minute, never an investigator's evening. And
/// a moderator approving one of the three lifts it out of the count, because the state changes:
/// the rule reads what the queue decided, not what the screener first said.</para>
///
/// <para><b>The pause is a window, not a flag.</b> Nothing is written to the account. Once the
/// oldest of the three refusals is more than <see cref="Window"/> old the pause ends on its own,
/// which means there is no switch for a moderator to forget to turn back off.</para>
/// </remarks>
public static class FeedMediaAbuse
{
    /// <summary>Confident refusals inside the window before uploads are paused.</summary>
    public const int RefusalsBeforePause = 3;

    /// <summary>How far back the refusals are counted.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>
    /// What the poster is told. Deliberately says nothing about which check tripped, for the
    /// same reason the review note is never shown to them.
    /// </summary>
    public const string PausedMessage =
        "Uploads from this account are paused for a day after repeated refusals. "
        + "Text posts still work, and a moderator can see why.";

    /// <summary>
    /// How many of this author's feed posts inside the window the screener confidently refused
    /// and nobody has since approved.
    /// </summary>
    public static Task<int> RecentRefusalsAsync(
        BenDataContext db, Guid authorId, DateTime nowUtc, CancellationToken ct)
    {
        var since = nowUtc - Window;
        return db.OrgMessages.AsNoTracking()
            .CountAsync(m => m.ChannelType == OrgMessageChannel.PublicFeed
                          && m.AuthorAppUserId == authorId
                          && m.MediaUploadFileId != null
                          && m.MediaReviewState == FeedMediaReviewState.Held
                          && m.MediaScreenerScore != null
                          && m.MediaScreenerScore >= NsfwDecision.BlockThreshold
                          && m.DateCreated >= since, ct);
    }

    /// <summary>Whether this author's media uploads are paused right now.</summary>
    public static async Task<bool> IsPausedAsync(
        BenDataContext db, Guid authorId, DateTime nowUtc, CancellationToken ct)
        => await RecentRefusalsAsync(db, authorId, nowUtc, ct) >= RefusalsBeforePause;
}
