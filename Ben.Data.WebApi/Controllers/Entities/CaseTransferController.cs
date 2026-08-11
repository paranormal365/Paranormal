using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Case transfer proposals — one org proposes, the other accepts/rejects.</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/cases/{caseId:guid}/transfers")]
[Authorize]
public sealed class CaseTransferController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public CaseTransferController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

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
                c.DateUpdated     = DateTime.UtcNow;
                c.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
            }
        }
        await db.SaveChangesAsync(ct);
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

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);
    }

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
