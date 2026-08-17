using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Aggregates everything waiting on the caller into one response, so the badge system costs a
/// single round trip rather than one call per messaging system.
/// </summary>
/// <remarks>
/// The three messaging systems track "read" differently and that difference is load-bearing here:
/// <list type="bullet">
///   <item><c>OrgMessage</c> has genuine per-user read state (<c>OrgMessageRecipient.DateRead</c>).</item>
///   <item><c>CaseMessage</c> tracks it per *side* (<c>IsReadByOrg</c>/<c>IsReadByClient</c>), not per user —
///   so an org-side count is "unread by the org", and any member opening the thread clears it for
///   all of them. That is the existing product behaviour, not something introduced here.</item>
///   <item><c>UserMessageTo</c> has per-recipient <c>DateLastRead</c>.</item>
/// </list>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class NotificationSummaryController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IOrganizationSecurityService _security;

    public NotificationSummaryController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IOrganizationSecurityService security)
    {
        _dbContextFactory = dbContextFactory;
        _security = security;
    }

    [HttpGet("notification-summary")]
    public async Task<ActionResult<NotificationSummaryResponse>> GetSummary(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // ── Internal org messages addressed to me ────────────────────────────
        var orgMessages = await BucketAsync(
            db.OrgMessageRecipients.AsNoTracking()
              .Where(r => r.RecipientAppUserId == userId && r.DateRead == null)
              .Select(r => (DateTime?)r.OrgMessage.DateCreated), ct);

        // ── Case messages awaiting an org reply, for orgs I actively belong to ──
        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        var caseMessagesAsOrg = myOrgIds.Count == 0
            ? NotificationBucket.Empty
            : await BucketAsync(
                db.CaseMessages.AsNoTracking()
                  .Where(m => m.SenderSide == CaseMessageSide.Client
                           && !m.IsReadByOrg
                           && myOrgIds.Contains(m.Case.OrganizationId))
                  .Select(m => (DateTime?)m.DateCreated), ct);

        // ── Case messages awaiting me as the client ──────────────────────────
        // "My cases" is both the ones I originated and the ones shared with me as a co-client.
        var myCaseIds = await db.Cases.AsNoTracking()
            .Where(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var coClientCaseIds = await db.CaseClientAccesses.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .Select(a => a.CaseId)
            .ToListAsync(ct);

        var clientCaseIds = myCaseIds.Union(coClientCaseIds).ToList();

        var caseMessagesAsClient = clientCaseIds.Count == 0
            ? NotificationBucket.Empty
            : await BucketAsync(
                db.CaseMessages.AsNoTracking()
                  .Where(m => m.SenderSide == CaseMessageSide.Organization
                           && !m.IsReadByClient
                           && clientCaseIds.Contains(m.CaseId))
                  .Select(m => (DateTime?)m.DateCreated), ct);

        // ── Platform/system messages ─────────────────────────────────────────
        var systemMessages = await BucketAsync(
            db.UserMessageTos.AsNoTracking()
              .Where(t => t.ToAppUserId == userId && t.DateLastRead == null)
              .Select(t => (DateTime?)t.UserMessage.DateCreated), ct);

        // ── File-permission requests waiting on me as the file owner ─────────
        var pendingRequests = await BucketAsync(
            db.UploadFilePermissionRequests.AsNoTracking()
              .Where(r => r.RequestStatus == FilePermissionRequestStatus.Pending
                       && db.UploadFiles.Any(f => f.Id == r.UploadFileId && f.AppUserId == userId))
              .Select(r => (DateTime?)r.DateCreated), ct);

        // ── Investigation invites I haven't answered ─────────────────────────
        // Only ones still ahead of me, and not cancelled: an unanswered RSVP for a visit that
        // has already happened (or been called off) is history, not a task. Bucketed on the
        // invite's DateCreated like everything else here — see the DTO for why the scheduled
        // date can't be the bucket timestamp.
        var nowUtc = DateTime.UtcNow;
        var investigationInvites = await BucketAsync(
            db.InvestigationAttendees.AsNoTracking()
              .Where(a => a.AppUserId == userId
                       && a.Rsvp == RsvpStatus.Invited
                       && a.Investigation.ScheduledDateTime > nowUtc
                       && a.Investigation.Status != InvestigationStatus.Cancelled)
              .Select(a => (DateTime?)a.DateCreated), ct);

        // ── Equipment loans wanting something from me ────────────────────────
        // Two different obligations in one bucket, because both mean "go and do something about a
        // piece of equipment": requests I have to decide on, and gear of mine that is overdue back.
        //
        // Personal gear resolves from the item's owner column. Group gear needs the group's
        // EquipmentCheckout permission, which is why the org list is filtered through the security
        // service first rather than joined — the permission can come from a role, a direct grant,
        // or being an owner/administrator, and only that service knows all three.
        var checkoutOrgIds = new List<Guid>();
        foreach (var orgId in myOrgIds)
        {
            if (await _security.HasAccessAsync(userId, orgId,
                    OrganizationSecurityTable.EquipmentCheckout, OrganizationSecurityAction.Update, ct))
                checkoutOrgIds.Add(orgId);
        }

        var equipmentCheckouts = await BucketAsync(
            db.EquipmentCheckouts.AsNoTracking()
              .Where(c =>
                  // Waiting on my decision.
                  (c.Status == EquipmentCheckoutStatus.Requested
                   && (c.EquipmentItem.OwnerAppUserId == userId
                       || (c.EquipmentItem.OwningOrganizationId != null
                           && checkoutOrgIds.Contains(c.EquipmentItem.OwningOrganizationId.Value))))
                  // Or out with somebody and late back to me.
                  || (c.Status == EquipmentCheckoutStatus.CheckedOut
                      && c.DateDue != null && c.DateDue < nowUtc
                      && (c.EquipmentItem.OwnerAppUserId == userId
                          || (c.EquipmentItem.OwningOrganizationId != null
                              && checkoutOrgIds.Contains(c.EquipmentItem.OwningOrganizationId.Value)))))
              .Select(c => (DateTime?)c.DateCreated), ct);

        return Ok(new NotificationSummaryResponse(
            orgMessages, caseMessagesAsOrg, caseMessagesAsClient, systemMessages, pendingRequests,
            investigationInvites, equipmentCheckouts));
    }

    /// <summary>
    /// Collapses a stream of arrival timestamps into a count plus the earliest of them, in one
    /// round trip. Returns <see cref="NotificationBucket.Empty"/> when the sequence is empty —
    /// GroupBy yields no row at all in that case, rather than a zero.
    /// </summary>
    private static async Task<NotificationBucket> BucketAsync(
        IQueryable<DateTime?> arrivals, CancellationToken ct)
    {
        var aggregate = await arrivals
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Oldest = g.Min() })
            .FirstOrDefaultAsync(ct);

        return aggregate is null
            ? NotificationBucket.Empty
            : new NotificationBucket(aggregate.Count, aggregate.Oldest);
    }
}
