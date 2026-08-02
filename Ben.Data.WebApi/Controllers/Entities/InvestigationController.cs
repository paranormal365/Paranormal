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

/// <summary>Investigation scheduling and attendee management for a case.</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/cases/{caseId:guid}/investigations")]
[Authorize]
public sealed class InvestigationController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public InvestigationController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<InvestigationRecord>>> GetAll(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var list = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .Where(i => i.CaseId == caseId)
            .OrderBy(i => i.ScheduledDateTime)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<InvestigationRecord>>(list));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvestigationRecord>> GetById(
        Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var inv = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        return inv is null ? NotFound() : Ok(_mapper.Map<InvestigationRecord>(inv));
    }

    [HttpPost]
    public async Task<ActionResult<InvestigationRecord>> Create(
        Guid orgId, Guid caseId, [FromBody] UpsertInvestigationRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct))
            return NotFound("Case not found.");

        var entity = new Investigation
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            OrgCalendarEventId  = request.OrgCalendarEventId,
            Title               = request.Title.Trim(),
            Description         = request.Description?.Trim(),
            Location            = request.Location?.Trim(),
            ScheduledDateTime   = request.ScheduledDateTime,
            EndDateTime         = request.EndDateTime,
            Status              = InvestigationStatus.Scheduled,
            EvidenceDueDate     = request.EvidenceDueDate,
            DateCreated         = DateTime.UtcNow,
            CreatedByAppUserId  = userId,
        };
        db.Investigations.Add(entity);
        await db.SaveChangesAsync(ct);
        var loaded = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstAsync(i => i.Id == entity.Id, ct);
        return CreatedAtAction(nameof(GetById), new { orgId, caseId, id = entity.Id },
            _mapper.Map<InvestigationRecord>(loaded));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvestigationRecord>> Update(
        Guid orgId, Guid caseId, Guid id, [FromBody] UpsertInvestigationRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.Investigations.FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        if (entity is null) return NotFound();
        entity.OrgCalendarEventId  = request.OrgCalendarEventId;
        entity.Title               = request.Title.Trim();
        entity.Description         = request.Description?.Trim();
        entity.Location            = request.Location?.Trim();
        entity.ScheduledDateTime   = request.ScheduledDateTime;
        entity.EndDateTime         = request.EndDateTime;
        entity.Status              = request.Status;
        entity.Notes               = request.Notes?.Trim();
        entity.EvidenceDueDate     = request.EvidenceDueDate;
        entity.DateUpdated         = DateTime.UtcNow;
        entity.UpdatedByAppUserId  = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        var loaded = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstAsync(i => i.Id == entity.Id, ct);
        return Ok(_mapper.Map<InvestigationRecord>(loaded));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.Investigations.FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        if (entity is null) return NotFound();
        db.Investigations.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Attendees ─────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/attendees")]
    public async Task<ActionResult<IEnumerable<InvestigationAttendeeRecord>>> GetAttendees(
        Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var attendees = await db.InvestigationAttendees.AsNoTracking()
            .Include(a => a.AppUser)
            .Where(a => a.InvestigationId == id)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<InvestigationAttendeeRecord>>(attendees));
    }

    [HttpPost("{id:guid}/attendees")]
    public async Task<ActionResult<InvestigationAttendeeRecord>> AddAttendee(
        Guid orgId, Guid caseId, Guid id, [FromBody] AddInvestigationAttendeeRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Investigations.AnyAsync(i => i.Id == id && i.CaseId == caseId, ct))
            return NotFound();
        var attendee = new InvestigationAttendee
        {
            Id = Guid.NewGuid(), InvestigationId = id, AppUserId = request.AppUserId,
            AssignedRole = request.AssignedRole?.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.InvestigationAttendees.Add(attendee);
        await db.SaveChangesAsync(ct);
        var loaded = await db.InvestigationAttendees.AsNoTracking()
            .Include(a => a.AppUser).FirstAsync(a => a.Id == attendee.Id, ct);
        return CreatedAtAction(nameof(GetAttendees), new { orgId, caseId, id },
            _mapper.Map<InvestigationAttendeeRecord>(loaded));
    }

    [HttpPut("{id:guid}/attendees/{attendeeId:guid}/attendance")]
    public async Task<ActionResult<InvestigationAttendeeRecord>> UpdateAttendance(
        Guid orgId, Guid caseId, Guid id, Guid attendeeId, [FromBody] UpdateAttendanceRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.InvestigationAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.InvestigationId == id, ct);
        if (attendee is null) return NotFound();
        attendee.DidAttend    = request.DidAttend;
        attendee.AssignedRole = request.AssignedRole?.Trim() ?? attendee.AssignedRole;
        if (request.Rsvp.HasValue) attendee.Rsvp = request.Rsvp.Value;
        await db.SaveChangesAsync(ct);
        var loaded = await db.InvestigationAttendees.AsNoTracking()
            .Include(a => a.AppUser).FirstAsync(a => a.Id == attendee.Id, ct);
        return Ok(_mapper.Map<InvestigationAttendeeRecord>(loaded));
    }

    [HttpDelete("{id:guid}/attendees/{attendeeId:guid}")]
    public async Task<IActionResult> RemoveAttendee(
        Guid orgId, Guid caseId, Guid id, Guid attendeeId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.InvestigationAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.InvestigationId == id, ct);
        if (attendee is null) return NotFound();
        db.InvestigationAttendees.Remove(attendee);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AnyAsync(
            m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
    }
}

// ── Evidence voting (separate route) ─────────────────────────────────────────

/// <summary>
/// Evidence voting on UploadFile items. Summary (counts only) is public.
/// Full voter details require org membership.
/// </summary>
[ApiController]
[Route("api/evidence-votes")]
public sealed class EvidenceVoteController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public EvidenceVoteController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    /// <summary>Returns the vote summary (counts only) for a file. Public — no auth.</summary>
    [HttpGet("{uploadFileId:guid}/summary")]
    [AllowAnonymous]
    public async Task<ActionResult<EvidenceVoteSummary>> GetSummary(
        Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var votes = await db.EvidenceVotes.AsNoTracking()
            .Where(v => v.UploadFileId == uploadFileId).ToListAsync(ct);

        var userId = GetCurrentUserId();
        EvidenceVoteType? myVote = userId == Guid.Empty
            ? null
            : votes.FirstOrDefault(v => v.VoterAppUserId == userId)?.VoteType;

        return Ok(new EvidenceVoteSummary(
            UploadFileId:      uploadFileId,
            ConfirmsCount:     votes.Count(v => v.VoteType == EvidenceVoteType.Confirms),
            DisputesCount:     votes.Count(v => v.VoteType == EvidenceVoteType.Disputes),
            InconclusiveCount: votes.Count(v => v.VoteType == EvidenceVoteType.Inconclusive),
            TotalVotes:        votes.Count,
            CurrentUserVote:   myVote));
    }

    /// <summary>Returns all votes with voter identities. Requires authentication.</summary>
    [HttpGet("{uploadFileId:guid}")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<EvidenceVoteRecord>>> GetAll(
        Guid uploadFileId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var votes = await db.EvidenceVotes.AsNoTracking()
            .Include(v => v.VoterAppUser)
            .Where(v => v.UploadFileId == uploadFileId)
            .OrderByDescending(v => v.DateVoted)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<EvidenceVoteRecord>>(votes));
    }

    /// <summary>Cast or update a vote on a piece of evidence.</summary>
    [HttpPost("{uploadFileId:guid}")]
    [Authorize]
    public async Task<ActionResult<EvidenceVoteSummary>> CastVote(
        Guid uploadFileId, [FromBody] CastEvidenceVoteRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == uploadFileId, ct))
            return NotFound("File not found.");

        // Determine if voter is a public user (not in any org)
        bool isPublic = !await db.OrganizationUserMemberships
            .AnyAsync(m => m.AppUserId == userId && m.IsActive, ct);

        Guid? voterOrgId = isPublic ? null : await db.OrganizationUserMemberships
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefaultAsync(ct);

        var existing = await db.EvidenceVotes
            .FirstOrDefaultAsync(v => v.UploadFileId == uploadFileId && v.VoterAppUserId == userId, ct);

        if (existing is not null)
        {
            existing.VoteType  = request.VoteType;
            existing.Comment   = request.Comment?.Trim();
            existing.DateVoted = DateTime.UtcNow;
        }
        else
        {
            db.EvidenceVotes.Add(new EvidenceVote
            {
                Id = Guid.NewGuid(), UploadFileId = uploadFileId, VoterAppUserId = userId,
                VoterOrganizationId = voterOrgId, VoteType = request.VoteType,
                Comment = request.Comment?.Trim(), IsPublicVoter = isPublic,
                DateVoted = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);

        // Return updated summary
        var votes = await db.EvidenceVotes.AsNoTracking()
            .Where(v => v.UploadFileId == uploadFileId).ToListAsync(ct);
        return Ok(new EvidenceVoteSummary(
            uploadFileId,
            votes.Count(v => v.VoteType == EvidenceVoteType.Confirms),
            votes.Count(v => v.VoteType == EvidenceVoteType.Disputes),
            votes.Count(v => v.VoteType == EvidenceVoteType.Inconclusive),
            votes.Count,
            request.VoteType));
    }

    /// <summary>Remove the current user's vote.</summary>
    [HttpDelete("{uploadFileId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveVote(Guid uploadFileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var vote = await db.EvidenceVotes
            .FirstOrDefaultAsync(v => v.UploadFileId == uploadFileId && v.VoterAppUserId == userId, ct);
        if (vote is null) return NotFound();
        db.EvidenceVotes.Remove(vote);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record UpsertInvestigationRequest(
    string Title,
    string? Description,
    string? Location,
    DateTime ScheduledDateTime,
    DateTime? EndDateTime,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    string? Notes,
    Guid? OrgCalendarEventId,
    DateTime? EvidenceDueDate = null);

public sealed record AddInvestigationAttendeeRequest(Guid AppUserId, string? AssignedRole);
public sealed record UpdateAttendanceRequest(bool? DidAttend, string? AssignedRole, Ben.Data.Common.Enums.RsvpStatus? Rsvp = null);
public sealed record CastEvidenceVoteRequest(
    Ben.Data.Common.Enums.EvidenceVoteType VoteType,
    string? Comment);
