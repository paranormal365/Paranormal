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
            r.CategoryMatchScore)).ToList());
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
            _screener.IsAutomatic));
    }
}
