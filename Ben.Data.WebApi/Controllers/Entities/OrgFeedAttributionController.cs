using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Feed;
using Ben.Service.Models.Feed;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A group's say over which feed posts link back to it (item 186 F7).
/// </summary>
/// <remarks>
/// <para>A post rendered from a group's case arrives here <b>Unclaimed</b>: credited to the
/// person, no group link, until somebody who administers the group decides. <b>Claim</b> puts
/// the group's name and link on the post and marks it "Group verified" — the group vouching the
/// footage is what it says, which is why a claim also writes a Confirmed labelled example into
/// the learning loop. <b>Decline</b> leaves the post up with no link, permanently deniable.</para>
///
/// <para>Deciding the other way later is allowed — a claim made in error is undone by declining,
/// and vice versa. The decision is one field on the post; the queue shows the history.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{orgId:guid}/feed-attributions")]
public sealed class OrgFeedAttributionController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;
    private readonly FeedLearningService _learning;

    public OrgFeedAttributionController(
        IDbContextFactory<BenDataContext> db,
        IOrganizationSecurityService security,
        FeedLearningService learning)
    {
        _db = db;
        _security = security;
        _learning = learning;
    }

    private async Task<bool> MayDecideAsync(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        return User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            || await _security.HasAccessAsync(userId, orgId,
                   OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct);
    }

    /// <summary>The queue: this group's case-derived posts, undecided first, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FeedAttributionItem>>> GetQueue(
        Guid orgId, CancellationToken ct)
    {
        if (!await MayDecideAsync(orgId, ct)) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = await db.OrgMessages.AsNoTracking()
            .Where(m => m.ChannelType == OrgMessageChannel.PublicFeed
                     && m.AttributedOrganizationId == orgId
                     && m.HiddenUtc == null)
            .OrderBy(m => m.AttributionState != OrgAttributionState.Unclaimed) // undecided first
            .ThenByDescending(m => m.DateCreated)
            .Take(200)
            .Select(m => new
            {
                m.Id,
                m.Body,
                m.AuthorAppUserId,
                AuthorName = m.AuthorAppUser.DisplayName ?? m.AuthorAppUser.Email,
                m.DateCreated,
                m.CaseId,
                CaseTitle = m.Case != null ? m.Case.Title : null,
                HasMedia = m.MediaUploadFileId != null
                        && m.MediaReviewState == FeedMediaReviewState.Approved,
                ContentType = m.MediaUploadFile != null ? m.MediaUploadFile.ContentType : null,
                TypeName = m.FeedExperienceType != null ? m.FeedExperienceType.Name : null,
                m.AttributionState,
                m.AttributionDecidedUtc,
                DeciderName = m.AttributionDecidedByAppUserId == null
                    ? null
                    : db.Users.Where(u => u.Id == m.AttributionDecidedByAppUserId)
                        .Select(u => u.DisplayName ?? u.Email).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new FeedAttributionItem(
            r.Id, r.Body, r.AuthorAppUserId, r.AuthorName ?? "Unknown", r.DateCreated,
            r.CaseId, r.CaseTitle,
            r.HasMedia,
            !r.HasMedia ? FeedMediaKind.None
                : r.ContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true
                    ? FeedMediaKind.Video : FeedMediaKind.Image,
            r.TypeName, r.AttributionState, r.AttributionDecidedUtc, r.DeciderName)).ToList());
    }

    /// <summary>The group puts its name on the post — and vouches for it.</summary>
    [HttpPost("{postId:guid}/claim")]
    public Task<IActionResult> Claim(Guid orgId, Guid postId, CancellationToken ct)
        => DecideAsync(orgId, postId, OrgAttributionState.Claimed, ct);

    /// <summary>The group says no. The post stays; the link never appears.</summary>
    [HttpPost("{postId:guid}/decline")]
    public Task<IActionResult> Decline(Guid orgId, Guid postId, CancellationToken ct)
        => DecideAsync(orgId, postId, OrgAttributionState.Declined, ct);

    private async Task<IActionResult> DecideAsync(
        Guid orgId, Guid postId, OrgAttributionState decision, CancellationToken ct)
    {
        if (!await MayDecideAsync(orgId, ct)) return NotFound();

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var post = await db.OrgMessages
            .FirstOrDefaultAsync(m => m.Id == postId
                                   && m.ChannelType == OrgMessageChannel.PublicFeed
                                   && m.AttributedOrganizationId == orgId, ct);
        if (post is null) return NotFound();

        var wasClaimed = post.AttributionState == OrgAttributionState.Claimed;
        post.AttributionState = decision;
        post.AttributionDecidedByAppUserId = userId;
        post.AttributionDecidedUtc = DateTime.UtcNow;

        // A claim is the group vouching the footage is what it says — the strongest label the
        // loop gets. Written once per transition into Claimed, not on every idempotent re-click.
        if (decision == OrgAttributionState.Claimed && !wasClaimed
            && post.FeedExperienceTypeId is { } typeId)
        {
            await _learning.AddExampleAsync(db, post.Id, typeId,
                FeedLabel.Confirmed, FeedLabelSource.GroupClaim, userId, ct);
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
