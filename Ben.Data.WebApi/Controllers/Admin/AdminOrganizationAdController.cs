using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The SuperAdmin review queue for group ads (item 166 W3).
/// </summary>
/// <remarks>
/// Approve puts the card into the public placements; reject records the reason. Both answers
/// message the group's administrators — a decision that sits silently in a table is the
/// write-only-feature shape this codebase keeps finding, and a group that hears nothing will
/// just submit the same ad again.
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/organization-ads")]
public sealed class AdminOrganizationAdController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;

    public AdminOrganizationAdController(
        IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages)
    {
        _dbFactory = dbFactory;
        _messages  = messages;
    }

    /// <summary>The queue first, then recent history — one list, review-ordered.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminOrganizationAdRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ads = await db.OrganizationAds.AsNoTracking()
            .Include(a => a.Organization)
            .OrderByDescending(a => a.Status == OrganizationAdStatus.Submitted)
            .ThenByDescending(a => a.DateSubmitted ?? a.DateCreated)
            .Take(100)
            .Select(a => new AdminOrganizationAdRecord(
                a.Id, a.OrganizationId, a.Organization.Name, a.Headline, a.Body,
                a.ImageUploadFileId, a.TargetKind, a.Status, a.RejectionReason,
                a.DateSubmitted, a.DateReviewed))
            .ToListAsync(ct);
        return Ok(ads);
    }

    [HttpPost("{adId:guid}/approve")]
    public async Task<IActionResult> Approve(Guid adId, CancellationToken ct)
        => await ReviewAsync(adId, approve: true, reason: null, ct);

    [HttpPost("{adId:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid adId, [FromBody] RejectOrganizationAdRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Give the group a reason — a bare rejection teaches them nothing.");
        return await ReviewAsync(adId, approve: false, request.Reason.Trim(), ct);
    }

    private async Task<IActionResult> ReviewAsync(Guid adId, bool approve, string? reason, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds
            .Include(a => a.Organization)
            .FirstOrDefaultAsync(a => a.Id == adId, ct);
        if (ad is null) return NotFound();
        if (ad.Status != OrganizationAdStatus.Submitted)
            return BadRequest("Only a submitted ad can be reviewed.");

        ad.Status = approve ? OrganizationAdStatus.Approved : OrganizationAdStatus.Rejected;
        ad.RejectionReason = reason;
        ad.DateReviewed = DateTime.UtcNow;
        ad.ReviewedByAppUserId = userId;
        ad.DateUpdated = ad.DateReviewed;
        ad.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        // The group hears the answer either way — see the class remarks.
        var admins = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == ad.OrganizationId && m.IsActive
                     && (m.Role == OrganizationMemberRole.Owner
                      || m.Role == OrganizationMemberRole.Administrator))
            .Select(m => m.AppUserId)
            .ToListAsync(ct);
        if (admins.Count > 0)
        {
            var subject = approve
                ? $"Your ad for {ad.Organization.Name} is live"
                : $"Your ad for {ad.Organization.Name} was not approved";
            var body = approve
                ? $"\"{ad.Headline}\" was approved and now appears in the group finder and on the home page, marked Promoted."
                : $"\"{ad.Headline}\" was declined: {reason}\n\nEdit the ad and submit it again whenever you're ready.";
            await _messages.SendAsync(subject, body, admins, userId, ct);
        }

        return NoContent();
    }
}

public sealed record RejectOrganizationAdRequest(string Reason);
