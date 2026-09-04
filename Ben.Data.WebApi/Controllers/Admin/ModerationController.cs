using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The moderator's desk: media waiting to be looked at, and the decisions about it (item 186 F5).
/// </summary>
/// <remarks>
/// <para><b>Not gated on the feed's feature switch</b>, exactly like the report queue next door.
/// Switching the feed off must not strand a queue of things somebody uploaded — the flag governs
/// the feature, this is the record of decisions about it.</para>
///
/// <para><b>Moderators, not administrators.</b> Reviewing what people post is a job somebody can
/// be trusted with without being trusted with billing, tiers or impersonation, and
/// <see cref="RoleNames.Moderator"/> exists so asking a volunteer to help is a small decision
/// rather than a large one. A SuperAdmin satisfies the policy implicitly.</para>
///
/// <para><b>Held is not deleted.</b> Refusing a photo leaves it on disk with a note about why, so
/// a decision can be revisited and a mistake undone. The one thing this endpoint cannot do is make
/// something disappear.</para>
/// </remarks>
[ApiController]
[Route("api/moderation")]
[Authorize(Policy = AuthPolicyNames.Moderator)]
public sealed class ModerationController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFeedMediaScreener _screener;
    private readonly FeedLearningService _learning;

    public ModerationController(
        IDbContextFactory<BenDataContext> db, IFeedMediaScreener screener, FeedLearningService learning)
    {
        _db = db;
        _screener = screener;
        _learning = learning;
    }

    /// <summary>
    /// Media awaiting a decision, oldest first.
    /// </summary>
    /// <param name="state">
    /// Which pile to show. Defaults to <see cref="FeedMediaReviewState.Pending"/> — the queue as
    /// such. <see cref="FeedMediaReviewState.Held"/> is the record of what was refused, which
    /// somebody should be able to read back and change their mind about.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// Oldest first, for the reason the report queue is: a queue worked newest-first leaves the
    /// oldest item unanswered for ever, and that is the person still waiting.
    /// </remarks>
    [HttpGet("feed-media")]
    public async Task<ActionResult<IReadOnlyList<FeedMediaReviewItem>>> GetFeedMedia(
        [FromQuery] FeedMediaReviewState? state, CancellationToken ct)
    {
        var wanted = state ?? FeedMediaReviewState.Pending;

        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = await db.OrgMessages.AsNoTracking()
            .Where(m => m.ChannelType == OrgMessageChannel.PublicFeed
                     && m.MediaUploadFileId != null
                     && m.MediaReviewState == wanted)
            .OrderBy(m => m.DateCreated)
            .Take(200)
            .Select(m => new
            {
                m.Id,
                m.AuthorAppUserId,
                AuthorName = m.AuthorAppUser.DisplayName ?? m.AuthorAppUser.Email,
                m.Body,
                m.DateCreated,
                m.MediaReviewState,
                m.MediaReviewNote,
                m.MediaReviewedUtc,
                m.FeedExperienceTypeId,
                ExperienceTypeName = m.FeedExperienceType != null ? m.FeedExperienceType.Name : null,
                m.CategoryMatchScore,
                ContentType = m.MediaUploadFile!.ContentType,
                ReviewerName = m.MediaReviewedByAppUserId == null
                    ? null
                    : db.Users.Where(u => u.Id == m.MediaReviewedByAppUserId)
                        .Select(u => u.DisplayName ?? u.Email).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Item 217: one count per author, so the queue can say "uploads paused" on the row a
        // moderator is looking at rather than leaving them to notice the same name three times.
        var now = DateTime.UtcNow;
        var refusalsByAuthor = new Dictionary<Guid, int>();
        foreach (var authorId in rows.Select(r => r.AuthorAppUserId).Distinct())
            refusalsByAuthor[authorId] = await FeedMediaAbuse.RecentRefusalsAsync(db, authorId, now, ct);

        return Ok(rows.Select(r => new FeedMediaReviewItem(
            r.Id,
            r.AuthorAppUserId,
            r.AuthorName ?? "Unknown",
            r.Body,
            r.DateCreated,
            r.MediaReviewState,
            r.MediaReviewNote,
            r.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
                ? FeedMediaKind.Video
                : FeedMediaKind.Image,
            $"/api/moderation/feed-media/{r.Id}/file",
            r.MediaReviewedUtc,
            r.ReviewerName,
            r.FeedExperienceTypeId,
            r.ExperienceTypeName,
            r.CategoryMatchScore,
            refusalsByAuthor.GetValueOrDefault(r.AuthorAppUserId))).ToList());
    }

    /// <summary>
    /// The moderator's judgment on a post's CATEGORY (item 186 F6): does the content match what
    /// the author says it shows? Separate from the safety decision on purpose — "safe to show"
    /// and "is what it says" are different questions, and this one writes a labelled example the
    /// re-fit learns from. The post itself is untouched: a mismatch is the author's to fix.
    /// </summary>
    [HttpPost("feed-categories/{postId:guid}")]
    public async Task<IActionResult> JudgeCategory(
        Guid postId, [FromBody] FeedCategoryVerdictRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var post = await db.OrgMessages.AsNoTracking()
            .Where(m => m.Id == postId && m.ChannelType == OrgMessageChannel.PublicFeed)
            .Select(m => new { m.FeedExperienceTypeId })
            .FirstOrDefaultAsync(ct);
        if (post is null) return NotFound();
        if (post.FeedExperienceTypeId is not { } typeId)
            return BadRequest("That post has no category to judge.");

        await _learning.AddExampleAsync(db, postId, typeId,
            request.Matches ? FeedLabel.Confirmed : FeedLabel.Mismatch,
            FeedLabelSource.Moderator, userId, ct);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// The file itself, for a moderator, whatever state it is in.
    /// </summary>
    /// <remarks>
    /// A separate route from the public one on purpose. The public route refuses anything that is
    /// not Approved — that is its whole job — so reviewing through it would be impossible. This
    /// one serves regardless of state and is behind the moderator policy, which is the only place
    /// unscreened media may be looked at.
    /// </remarks>
    [HttpGet("feed-media/{postId:guid}/file")]
    public async Task<IActionResult> GetFeedMediaFile(
        Guid postId,
        [FromServices] IMediaIngestService mediaIngest,
        [FromServices] IFileStorageService storage,
        CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var file = await db.OrgMessages.AsNoTracking()
            .Where(m => m.Id == postId
                     && m.ChannelType == OrgMessageChannel.PublicFeed
                     && m.MediaUploadFileId != null)
            .Select(m => new { m.MediaUploadFile!.StoragePath, m.MediaUploadFile.ContentType })
            .FirstOrDefaultAsync(ct);

        if (file?.StoragePath is not { Length: > 0 } storagePath) return NotFound();

        // Relative to the storage root — see the note on the public route in FeedController.
        var servingPath = mediaIngest.ServingPathFor(storagePath);
        if (!storage.Exists(servingPath)) return NotFound();

        return File(await storage.OpenReadAsync(servingPath, ct),
                    file.ContentType ?? "application/octet-stream",
                    enableRangeProcessing: true);
    }

    /// <summary>Approves or holds one post's media.</summary>
    /// <remarks>
    /// Idempotent in the sense that matters: deciding the same way twice changes only the
    /// timestamp, and deciding the other way is how a mistake is undone.
    /// </remarks>
    [HttpPost("feed-media/{postId:guid}")]
    public async Task<IActionResult> ReviewFeedMedia(
        Guid postId, [FromBody] ReviewFeedMediaRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var post = await db.OrgMessages
            .FirstOrDefaultAsync(m => m.Id == postId
                                   && m.ChannelType == OrgMessageChannel.PublicFeed
                                   && m.MediaUploadFileId != null, ct);
        if (post is null) return NotFound();

        post.MediaReviewState = request.Approve
            ? FeedMediaReviewState.Approved
            : FeedMediaReviewState.Held;
        post.MediaReviewNote = string.IsNullOrWhiteSpace(request.Note)
            ? post.MediaReviewNote
            : request.Note.Trim();
        post.MediaReviewedByAppUserId = userId;
        post.MediaReviewedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Published sessions whose media is waiting on a person, oldest first.</summary>
    /// <remarks>
    /// Separate from the feed queue rather than merged into it: a field session is a night's
    /// recording at a place, not a post, and a reviewer deciding about one is answering a
    /// different question — "should strangers see the inside of this building" rather than
    /// "should this image be on a public feed".
    /// </remarks>
    [HttpGet("archive-media")]
    public async Task<ActionResult<IReadOnlyList<ArchiveMediaReviewRow>>> GetArchiveMedia(
        [FromQuery] bool includeHeld, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        return Ok(await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.PublishedAtUtc != null
                     && s.Files.Any()
                     && (s.MediaReviewState == FeedMediaReviewState.Pending
                         || (includeHeld && s.MediaReviewState == FeedMediaReviewState.Held)))
            .OrderBy(s => s.PublishedAtUtc)
            .Select(s => new ArchiveMediaReviewRow(
                s.Id,
                s.SubmittedByAppUser.DisplayName ?? s.SubmittedByAppUser.Email ?? "Unknown",
                s.Place!.Name,
                s.PlaceId!.Value,
                s.LocationLabel,
                s.StartedAt,
                s.PublishedAtUtc!.Value,
                s.Files.Count,
                s.MediaReviewState,
                s.MediaReviewNote))
            .ToListAsync(ct));
    }

    /// <summary>Approves or holds one published session's media.</summary>
    /// <remarks>
    /// Idempotent in the way that matters, like the feed's: deciding the same way twice moves
    /// only the timestamp, and deciding the other way is how a mistake is undone.
    /// </remarks>
    [HttpPost("archive-media/{sessionId:guid}")]
    public async Task<IActionResult> ReviewArchiveMedia(
        Guid sessionId, [FromBody] ReviewFeedMediaRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var session = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();

        session.MediaReviewState = request.Approve
            ? FeedMediaReviewState.Approved
            : FeedMediaReviewState.Held;
        session.MediaReviewNote = string.IsNullOrWhiteSpace(request.Note)
            ? session.MediaReviewNote
            : request.Note.Trim();
        session.MediaReviewedByAppUserId = userId;
        session.MediaReviewedUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>How much is waiting, and whether anything is screening it automatically.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<FeedModerationSummary>> GetSummary(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var media = db.OrgMessages.AsNoTracking()
            .Where(m => m.ChannelType == OrgMessageChannel.PublicFeed && m.MediaUploadFileId != null);

        return Ok(new FeedModerationSummary(
            await media.CountAsync(m => m.MediaReviewState == FeedMediaReviewState.Pending, ct),
            await media.CountAsync(m => m.MediaReviewState == FeedMediaReviewState.Held, ct),
            await db.OrgMessageReports.AsNoTracking()
                .CountAsync(r => r.Outcome == FeedReportOutcome.Pending, ct),
            _screener.IsAutomatic,
            // The dark-launch facts (item 186 F10): is the feature on, and is content
            // accumulating behind it. The reminder banner pivots on exactly these two.
            await Services.SiteSettingsService.GetBoolAsync(
                db, Services.SiteSettingKeys.FeaturePublicFeed, whenUnset: false, ct),
            await db.OrgMessages.AsNoTracking()
                .CountAsync(m => m.ChannelType == OrgMessageChannel.PublicFeed, ct)));
    }
}

/// <summary>One published session waiting on a reviewer's decision about its media.</summary>
/// <remarks>
/// Carries the place and the contributor because that is what the decision turns on: a night at a
/// public landmark and a night somewhere a reviewer does not recognise are different questions,
/// and the readings are already public either way.
/// </remarks>
public sealed record ArchiveMediaReviewRow(
    Guid SessionId,
    string ContributorName,
    string? PlaceName,
    Guid PlaceId,
    string? LocationLabel,
    DateTime StartedAt,
    DateTime PublishedAtUtc,
    int FileCount,
    Ben.Data.Common.Enums.FeedMediaReviewState State,
    string? Note);
