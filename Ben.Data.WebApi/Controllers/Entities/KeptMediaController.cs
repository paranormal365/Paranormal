using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Media;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Stops the clock on a file a business wants to keep (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> photographs last a month and recordings a week "unless they mark
/// them to be saved". This is the marking. Without it the retention rules were a deletion
/// schedule with no exception, which is not what was asked for — and a recording had no way to be
/// kept at all, because the only keep that existed was putting a picture in a tour's gallery.</para>
///
/// <para><b>The business decides, not the person who uploaded it.</b> The clock belongs to the
/// plan the business is paying for. Somebody who wants their own copy downloads it — the file is
/// theirs and every screen that lists it offers that.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/kept-media")]
public sealed class KeptMediaController : OrgCmsControllerBase
{
    public KeptMediaController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    /// <summary>Keeps a file for good.</summary>
    [HttpPost("{uploadFileId:guid}")]
    public Task<IActionResult> Keep(Guid orgId, Guid uploadFileId, CancellationToken ct)
        => SetAsync(orgId, uploadFileId, keep: true, ct);

    /// <summary>
    /// Lets a file go back on the clock.
    /// </summary>
    /// <remarks>
    /// It restarts from now rather than from the upload, so releasing something kept six months
    /// ago does not delete it in the same second. Whoever uploaded it is warned first either way.
    /// </remarks>
    [HttpDelete("{uploadFileId:guid}")]
    public Task<IActionResult> Release(Guid orgId, Guid uploadFileId, CancellationToken ct)
        => SetAsync(orgId, uploadFileId, keep: false, ct);

    private async Task<IActionResult> SetAsync(Guid orgId, Guid uploadFileId, bool keep, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        // The file has to be one this business actually holds — the same question the retention
        // policy asks when it decides whose clock governs it, so a business can only keep what it
        // could have deleted.
        if (await MediaRetentionPolicy.OrganizationForAsync(db, uploadFileId, ct) != orgId)
            return NotFound();

        var file = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (file is null) return NotFound();

        var now = DateTime.UtcNow;
        if (keep)
        {
            file.KeptAtUtc = now;
            file.KeptByAppUserId = userId;
            file.ExpiresAtUtc = null;
        }
        else
        {
            file.KeptAtUtc = null;
            file.KeptByAppUserId = null;

            // Back on the clock from today. The warning machinery then does its work again, so
            // nobody loses a file to a change of mind without being told.
            var rules = await new MediaRetentionPolicy(
                HttpContext.RequestServices.GetRequiredService<Services.Billing.SubscriptionLimitGuard>())
                .RulesForAsync(orgId, ct);
            file.ExpiresAtUtc = MediaRetentionPolicy.ExpiryFor(rules, file.ContentType, now);
            file.ExpiryNoticeSentAtUtc = null;
        }

        file.DateUpdated = now;
        file.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }
}
