using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Case transfer proposals — one org proposes, the other accepts/rejects.</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/cases/{caseId:guid}/transfers")]
[Authorize]
public sealed class CaseTransferController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    private readonly Services.PlatformMessageService _messages;

    public CaseTransferController(
        IDbContextFactory<BenDataContext> db, IMapper mapper, Services.PlatformMessageService messages,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _mapper = mapper; _messages = messages; _security = security; }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseTransferLogRecord>>> GetAll(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var logs = await db.CaseTransferLogs.AsNoTracking()
            .Include(l => l.FromOrganization).Include(l => l.ToOrganization).Include(l => l.ProposedByAppUser)
            .Where(l => l.CaseId == caseId)
            .OrderByDescending(l => l.DateProposed)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<CaseTransferLogRecord>>(logs));
    }

    /// <summary>
    /// Transfers waiting on THIS organization's answer — the receiving side's inbox.
    /// </summary>
    /// <remarks>
    /// Until this existed the receiving group had no surface at all: the pending log sat in a
    /// table only the SENDER's case panel could list, because the per-case list requires the case
    /// to already belong to you. Another write-only feature, found the moment the client-proposed
    /// flow (item 84) needed the receiver to actually answer. Route is org-level and deliberately
    /// not under /cases/{caseId} — the whole point is that the case is not yours yet.
    /// </remarks>
    [HttpGet("/api/organizations/{orgId:guid}/incoming-transfers")]
    public async Task<ActionResult<IEnumerable<IncomingTransferRecord>>> GetIncoming(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);

        var rows = await db.CaseTransferLogs.AsNoTracking()
            .Where(l => l.ToOrganizationId == orgId && l.Status == CaseTransferStatus.Pending)
            .OrderBy(l => l.DateProposed)
            .Select(l => new IncomingTransferRecord(
                l.Id, l.CaseId, l.Case.Title, l.Case.City, l.Case.State,
                l.FromOrganization.Name,
                l.ProposedByClient,
                l.ProposedByClient ? l.ShareHistory : true,
                l.ProposedByClient ? l.ShareInvestigations : true,
                l.TransferReason, l.DateProposed))
            .ToListAsync(ct);

        return Ok(rows);
    }

    public sealed record IncomingTransferRecord(
        Guid LogId, Guid CaseId, string CaseTitle, string City, string State,
        string FromOrganizationName, bool ProposedByClient,
        bool ShareHistory, bool ShareInvestigations,
        string? Reason, DateTime DateProposed);

    /// <summary>Propose transferring this case to another organization.</summary>
    [HttpPost]
    public async Task<ActionResult<CaseTransferLogRecord>> Propose(
        Guid orgId, Guid caseId, [FromBody] ProposeCaseTransferRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId && x.OrganizationId == orgId, ct);
        if (c is null) return NotFound("Case not found in this organization.");
        if (!await db.Organizations.AnyAsync(o => o.Id == request.ToOrganizationId, ct))
            return BadRequest("Target organization not found.");

        // ── Item 167, both ends of Ben's rule at the SENDING door ────────────────
        // A group whose plan lacks case transfers can neither hand a case out nor be handed
        // one — and the receiving end is re-checked at acceptance, because a plan can change
        // while a proposal waits. Client-proposed transfers are gated elsewhere and only on
        // the RECEIVING end: the case is the client's, and a group's plan must not hold it.
        var (senderMay, senderTier) = await Ben.Data.Source.Services.TierAreaResolution
            .HasCapabilityAsync(db, orgId, Ben.Data.Common.Enums.TierCapability.CaseTransfers, ct);
        if (!senderMay)
            return BadRequest(
                $"Your group's plan{(senderTier is null ? "" : $" ({senderTier})")} does not include case transfers. "
                + "See the Pricing page for what each plan includes.");

        var (receiverMay, receiverTier) = await Ben.Data.Source.Services.TierAreaResolution
            .HasCapabilityAsync(db, request.ToOrganizationId, Ben.Data.Common.Enums.TierCapability.CaseTransfers, ct);
        if (!receiverMay)
            return BadRequest(
                $"That group's plan{(receiverTier is null ? "" : $" ({receiverTier})")} does not include case "
                + "transfers, so a case cannot be sent to them.");

        var log = new CaseTransferLog
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            FromOrganizationId   = orgId,
            ToOrganizationId     = request.ToOrganizationId,
            ProposedByAppUserId  = userId,
            Status               = CaseTransferStatus.Pending,
            TransferReason       = request.TransferReason?.Trim(),
            DateProposed         = DateTime.UtcNow,
        };
        db.CaseTransferLogs.Add(log);
        c.Status = CaseStatus.Transferred;
        await db.SaveChangesAsync(ct);

        var loaded = await db.CaseTransferLogs.AsNoTracking()
            .Include(l => l.FromOrganization).Include(l => l.ToOrganization).Include(l => l.ProposedByAppUser)
            .FirstAsync(l => l.Id == log.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { orgId, caseId },
            _mapper.Map<CaseTransferLogRecord>(loaded));
    }

    /// <summary>
    /// Respond to an incoming transfer — the receiving org accepts or rejects.
    /// On acceptance: Case.OrganizationId is changed to this org + new case number assigned.
    /// </summary>
    [HttpPut("{logId:guid}/respond")]
    public async Task<ActionResult<CaseTransferLogRecord>> Respond(
        Guid orgId, Guid caseId, Guid logId, [FromBody] RespondTransferRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var log = await db.CaseTransferLogs
            .Include(l => l.FromOrganization).Include(l => l.ToOrganization).Include(l => l.ProposedByAppUser)
            .FirstOrDefaultAsync(l => l.Id == logId && l.ToOrganizationId == orgId, ct);
        if (log is null) return NotFound();
        if (log.Status != CaseTransferStatus.Pending) return BadRequest("Already responded.");

        // ── Item 167 at the RECEIVING door ───────────────────────────────────────
        // Checked at the moment the case actually moves, not just at proposal: the receiving
        // group's plan may have changed while the proposal waited. Rejecting stays free —
        // declining work must never require a plan.
        if (request.Accept)
        {
            var (receiverMay, receiverTier) = await Ben.Data.Source.Services.TierAreaResolution
                .HasCapabilityAsync(db, orgId, Ben.Data.Common.Enums.TierCapability.CaseTransfers, ct);
            if (!receiverMay)
                return BadRequest(
                    $"Your group's plan{(receiverTier is null ? "" : $" ({receiverTier})")} does not include case "
                    + "transfers, so this case cannot be accepted. See the Pricing page for what each plan includes.");

            // Item 184: accepting a PRIVATE case is taking on private-lane work, so the
            // receiving plan must include it — same moment-of-movement rule as above.
            var isPrivate = await db.Cases.AsNoTracking()
                .AnyAsync(x => x.Id == caseId && x.IsPrivateEngagement, ct);
            if (isPrivate && await Ben.Data.WebApi.Services.PrivateCaseGate.RefusalAsync(db, orgId, ct) is { } noPrivate)
                return BadRequest(noPrivate);
        }

        log.Status               = request.Accept ? CaseTransferStatus.Accepted : CaseTransferStatus.Rejected;
        log.RespondedByAppUserId = userId == Guid.Empty ? null : userId;
        log.RejectionReason      = request.Accept ? null : request.Reason?.Trim();
        log.DateResponded        = DateTime.UtcNow;

        if (request.Accept)
        {
            var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
            if (c is not null)
            {
                // Assign a new case number in the receiving org
                int year = DateTime.UtcNow.Year;
                int max  = await db.Cases
                    .Where(x => x.OrganizationId == orgId && x.CaseYear == year)
                    .MaxAsync(x => (int?)x.OrgCaseNumber, ct) ?? 0;
                c.OrganizationId  = orgId;
                c.CaseYear        = year;
                c.OrgCaseNumber   = max + 1;
                c.Status          = CaseStatus.Accepted;
                c.StatusBeforePause = null;   // a fresh start, not a suspended old one
                c.DateUpdated     = DateTime.UtcNow;
                c.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

                // ── the client's consent, enforced at the moment it matters (item 84) ──
                if (log.ProposedByClient)
                {
                    if (!log.ShareHistory)
                    {
                        // The collected history stays the client's: re-scoped to ClientOnly, which
                        // this org's timeline never returns. Public entries stay public — they
                        // were already published to the world, and "withhold from the new group"
                        // cannot mean less than that.
                        var withheld = await db.CaseTimelineEntries
                            .Where(e => e.CaseId == caseId
                                     && e.Visibility != CaseTimelineVisibility.Public)
                            .ToListAsync(ct);
                        foreach (var e in withheld)
                            e.Visibility = CaseTimelineVisibility.ClientOnly;
                    }

                    if (!log.ShareInvestigations)
                    {
                        // Not shared means not carried: the original group's investigations detach
                        // from the case and remain that group's flat records — "findings remain
                        // the original group's". Nothing is deleted, nothing is copied.
                        var withheld = await db.Investigations
                            .Where(i => i.CaseId == caseId && i.OrganizationId == log.FromOrganizationId)
                            .ToListAsync(ct);
                        foreach (var inv in withheld)
                            inv.CaseId = null;
                    }
                    // Shared investigations need no code at all: the case moved, their CaseId
                    // still points at it, so the new group reads them through the case while the
                    // original group keeps them in its own list — dual visibility for dual
                    // ownership, no copy made.
                }
            }
        }
        await db.SaveChangesAsync(ct);

        // A client-proposed move ends with the client hearing the answer — from the platform,
        // immediately, whichever way it went.
        if (log.ProposedByClient)
        {
            var caseTitle = await db.Cases.AsNoTracking()
                .Where(x => x.Id == caseId).Select(x => x.Title).FirstOrDefaultAsync(ct) ?? "your case";

            await _messages.SendAsync(
                request.Accept
                    ? $"{log.ToOrganization.Name} accepted your case"
                    : $"{log.ToOrganization.Name} declined your case",
                request.Accept
                    ? $"{log.ToOrganization.Name} has taken over \"{caseTitle}\". The case is "
                    + "active again, and what they can see of the previous group's work follows "
                    + "the choices you made."
                    : $"{log.ToOrganization.Name} declined to take \"{caseTitle}\""
                    + (string.IsNullOrWhiteSpace(log.RejectionReason) ? "." : $": {log.RejectionReason}")
                    + " Your case remains paused, and you can ask a different organization.",
                [log.ProposedByAppUserId], userId, ct);
        }

        return Ok(_mapper.Map<CaseTransferLogRecord>(log));
    }

    /// <summary>Cancel an outgoing pending transfer proposed by this organization.</summary>
    [HttpPut("{logId:guid}/cancel")]
    public async Task<ActionResult<CaseTransferLogRecord>> Cancel(
        Guid orgId, Guid caseId, Guid logId, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);

        var log = await db.CaseTransferLogs
            .Include(l => l.FromOrganization).Include(l => l.ToOrganization).Include(l => l.ProposedByAppUser)
            .FirstOrDefaultAsync(l => l.Id == logId && l.FromOrganizationId == orgId, ct);
        if (log is null) return NotFound();
        if (log.Status != CaseTransferStatus.Pending) return BadRequest("Transfer is not pending.");

        log.Status        = CaseTransferStatus.Cancelled;
        log.DateResponded = DateTime.UtcNow;

        // Restore case status to Accepted (it was set to Transferred on proposal)
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c?.Status == CaseStatus.Transferred)
            c.Status = CaseStatus.Accepted;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<CaseTransferLogRecord>(log));
    }

    // Item 156 Phase D: bare membership stopped being the rule here — see CaseFileController.
    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);

    private async Task<bool> IsOrgAdminAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgAdminAsync(db, orgId, userId, ct);
    }
}

public sealed record ProposeCaseTransferRequest(Guid ToOrganizationId, string? TransferReason);
public sealed record RespondTransferRequest(bool Accept, string? Reason);
