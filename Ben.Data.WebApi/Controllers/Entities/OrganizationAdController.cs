using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A group's own promotional ad (item 166 W3): draft it, submit it for review, withdraw it.
/// </summary>
/// <remarks>
/// <para>Gated on Organization-Update — the same bar as editing the group itself, which the
/// Owner/Administrator bypass satisfies. One ad that is not Rejected per group: a group's
/// promotion is a single card, and "which of our three ads is live?" is a question nobody
/// should ever have to ask.</para>
///
/// <para>The review chain's invariant lives elsewhere too, but starts here: nothing a group
/// writes is public until a SuperAdmin approves it, and any EDIT of a submitted or approved
/// ad drops it back to Draft — the reviewed text is the approved text, never a moving target.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{orgId:guid}/ads")]
public sealed class OrganizationAdController : BenControllerBase
{
    private static readonly string[] TargetKinds = ["org", "find"];

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IOrganizationSecurityService _security;
    private readonly IAuditLogService _auditLog;

    public OrganizationAdController(
        IDbContextFactory<BenDataContext> dbFactory,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
    {
        _dbFactory = dbFactory;
        _security  = security;
        _auditLog  = auditLog;
    }

    private async Task<bool> MayManageAsync(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return false;
        return User.IsInRole(RoleNames.SuperAdmin)
            || await _security.HasAccessAsync(userId, orgId,
                   OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct);
    }

    private static OrganizationAdRecord ToRecord(OrganizationAd ad) => new(
        ad.Id, ad.OrganizationId, ad.Headline, ad.Body, ad.ImageUploadFileId, ad.TargetKind,
        ad.Status, ad.RejectionReason, ad.DateSubmitted, ad.DateReviewed, ad.DateCreated);

    private static string? Validate(SaveOrganizationAdRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Headline)) return "Give the ad a headline.";
        if (request.Headline.Trim().Length > 80) return "The headline can be at most 80 characters.";
        if (string.IsNullOrWhiteSpace(request.Body)) return "Say something about the group — the body is empty.";
        if (request.Body.Trim().Length > 300) return "The body can be at most 300 characters.";
        if (!TargetKinds.Contains(request.TargetKind))
            return "The ad can lead to your public page or the group finder — nowhere else.";
        return null;
    }

    /// <summary>Every ad the group has, newest first — the live one and its history.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationAdRecord>>> GetAll(Guid orgId, CancellationToken ct)
    {
        if (!await MayManageAsync(orgId, ct)) return Forbid();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ads = await db.OrganizationAds.AsNoTracking()
            .Where(a => a.OrganizationId == orgId)
            .OrderByDescending(a => a.DateCreated)
            .ToListAsync(ct);
        return Ok(ads.Select(ToRecord));
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationAdRecord>> Create(
        Guid orgId, [FromBody] SaveOrganizationAdRequest request, CancellationToken ct)
    {
        if (!await MayManageAsync(orgId, ct)) return Forbid();
        if (Validate(request) is { } problem) return BadRequest(problem);

        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct)) return NotFound();

        // One card per group. Rejected ads are history and don't block a fresh start.
        if (await db.OrganizationAds.AnyAsync(a =>
                a.OrganizationId == orgId && a.Status != OrganizationAdStatus.Rejected, ct))
            return BadRequest("Your group already has an ad — edit or withdraw that one instead of starting another.");

        var ad = new OrganizationAd
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Headline = request.Headline.Trim(), Body = request.Body.Trim(),
            ImageUploadFileId = request.ImageUploadFileId, TargetKind = request.TargetKind,
            Status = OrganizationAdStatus.Draft,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrganizationAds.Add(ad);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationAd), ad.Id, ad, userId, AppSources.WebApi));
        return Ok(ToRecord(ad));
    }

    /// <summary>Edits the ad. An edit to a submitted or approved ad pulls it back to Draft —
    /// the reviewed text is the approved text, never a moving target.</summary>
    [HttpPut("{adId:guid}")]
    public async Task<ActionResult<OrganizationAdRecord>> Update(
        Guid orgId, Guid adId, [FromBody] SaveOrganizationAdRequest request, CancellationToken ct)
    {
        if (!await MayManageAsync(orgId, ct)) return Forbid();
        if (Validate(request) is { } problem) return BadRequest(problem);

        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds
            .FirstOrDefaultAsync(a => a.Id == adId && a.OrganizationId == orgId, ct);
        if (ad is null) return NotFound();

        ad.Headline = request.Headline.Trim();
        ad.Body = request.Body.Trim();
        ad.ImageUploadFileId = request.ImageUploadFileId;
        ad.TargetKind = request.TargetKind;
        ad.Status = OrganizationAdStatus.Draft;
        ad.RejectionReason = null;
        ad.DateSubmitted = null;
        ad.DateReviewed = null;
        ad.ReviewedByAppUserId = null;
        ad.DateUpdated = DateTime.UtcNow;
        ad.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ToRecord(ad));
    }

    /// <summary>Sends the draft to the review queue.</summary>
    [HttpPost("{adId:guid}/submit")]
    public async Task<ActionResult<OrganizationAdRecord>> Submit(Guid orgId, Guid adId, CancellationToken ct)
    {
        if (!await MayManageAsync(orgId, ct)) return Forbid();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds
            .FirstOrDefaultAsync(a => a.Id == adId && a.OrganizationId == orgId, ct);
        if (ad is null) return NotFound();
        if (ad.Status != OrganizationAdStatus.Draft)
            return BadRequest("Only a draft can be submitted.");

        ad.Status = OrganizationAdStatus.Submitted;
        ad.DateSubmitted = DateTime.UtcNow;
        ad.DateUpdated = ad.DateSubmitted;
        ad.UpdatedByAppUserId = GetCurrentUserId();
        await db.SaveChangesAsync(ct);
        return Ok(ToRecord(ad));
    }

    /// <summary>Pulls the ad out of review or out of the placements, back to Draft.</summary>
    [HttpPost("{adId:guid}/withdraw")]
    public async Task<ActionResult<OrganizationAdRecord>> Withdraw(Guid orgId, Guid adId, CancellationToken ct)
    {
        if (!await MayManageAsync(orgId, ct)) return Forbid();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds
            .FirstOrDefaultAsync(a => a.Id == adId && a.OrganizationId == orgId, ct);
        if (ad is null) return NotFound();
        if (ad.Status is not (OrganizationAdStatus.Submitted or OrganizationAdStatus.Approved))
            return BadRequest("Only a submitted or approved ad can be withdrawn.");

        ad.Status = OrganizationAdStatus.Draft;
        ad.DateUpdated = DateTime.UtcNow;
        ad.UpdatedByAppUserId = GetCurrentUserId();
        await db.SaveChangesAsync(ct);
        return Ok(ToRecord(ad));
    }

    [HttpDelete("{adId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid adId, CancellationToken ct)
    {
        if (!await MayManageAsync(orgId, ct)) return Forbid();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds
            .FirstOrDefaultAsync(a => a.Id == adId && a.OrganizationId == orgId, ct);
        if (ad is null) return NotFound();

        db.OrganizationAds.Remove(ad);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
