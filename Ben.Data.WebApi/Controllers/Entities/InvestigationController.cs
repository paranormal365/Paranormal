using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.WebApi.Services.Access;

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
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var list = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .Where(i => i.CaseId == caseId)
            .OrderBy(i => i.ScheduledDateTime)
            .ToListAsync(ct);

        // Batched, not per row: the panel renders manage controls from this and a query each
        // would be one per investigation on every case page.
        var flags = await InvestigationAccess.ComputeFlagsAsync(
            db, orgId, list.Select(i => i.Id).ToList(),
            GetCurrentUserId(), User.IsInRole(RoleNames.SuperAdmin), ct);

        var records = _mapper.Map<IEnumerable<InvestigationRecord>>(list)
            .Select(r => r with { CanEditRecord = flags[r.Id].CanEditRecord });

        return Ok(records);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvestigationRecord>> GetById(
        Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var inv = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        if (inv is null) return NotFound();

        return Ok(_mapper.Map<InvestigationRecord>(inv) with
        {
            CanEditRecord = await CanManageAsync(id, ct),
        });
    }

    [HttpPost]
    public async Task<ActionResult<InvestigationRecord>> Create(
        Guid orgId, Guid caseId, [FromBody] UpsertInvestigationRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct))
            return NotFound("Case not found.");

        var entity = new Investigation
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            // Required now that an investigation can outlive its case link. The case has already
            // been proved to belong to this org by the check above, so the route id is safe here.
            OrganizationId      = orgId,
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

        // Inherits the case's place unless the caller named another. This is what finally writes
        // the coordinate columns added by AddInvestigationCoordinates, which nothing had ever set.
        var placement = await InvestigationPlacement.ApplyAsync(
            db, entity, request.PlaceId, request.NewPlace, userId, ct);
        if (placement.Error is not null) return BadRequest(placement.Error);

        // The sharing scope follows the place unless the caller states one. A case-bound visit is
        // at somebody's home more often than not, so the cautious default is also the common one.
        entity.Visibility = request.Visibility ?? InvestigationVisibilityFilter.DefaultFor(placement.Place);
        if (InvestigationVisibilityFilter.Reject(entity.Visibility, placement.Place) is { } scopeError)
            return BadRequest(scopeError);

        // Auto-create an org calendar event if none was supplied
        if (entity.OrgCalendarEventId is null)
        {
            var calEvent = new Ben.Data.Source.Entities.OrgCalendarEvent
            {
                Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId,
                Title = $"Investigation: {entity.Title}",
                Description = entity.Description,
                Location = entity.Location,
                StartDateTime = entity.ScheduledDateTime,
                EndDateTime = entity.EndDateTime ?? entity.ScheduledDateTime.AddHours(2),
                IsAllDay = false, IsPublic = false,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            db.OrgCalendarEvents.Add(calEvent);
            entity.OrgCalendarEventId = calEvent.Id;
        }

        await db.SaveChangesAsync(ct);
        var loaded = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstAsync(i => i.Id == entity.Id, ct);
        // The creator can always manage what they just scheduled — one of the five rules, so this
        // needs no second query either.
        return CreatedAtAction(nameof(GetById), new { orgId, caseId, id = entity.Id },
            _mapper.Map<InvestigationRecord>(loaded) with { CanEditRecord = true });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvestigationRecord>> Update(
        Guid orgId, Guid caseId, Guid id, [FromBody] UpsertInvestigationRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var entity = await db.Investigations.FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        if (entity is null) return NotFound();

        // Gated after the lookup, not before: membership is already proved above, so reading the
        // row first leaks nothing, and a missing investigation deserves "not found" rather than
        // "you may not edit the thing that does not exist".
        if (!await CanManageAsync(id, ct)) return Forbid();

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

        // Re-placed on update too — editing the location and watching the map not move is exactly
        // the sort of silent staleness the GeocodeNote convention exists to prevent.
        var placement = await InvestigationPlacement.ApplyAsync(
            db, entity, request.PlaceId, request.NewPlace, userId, ct);
        if (placement.Error is not null) return BadRequest(placement.Error);

        // Only changed when the caller says so: an edit that says nothing about sharing should not
        // silently re-derive a scope somebody may have deliberately narrowed.
        if (request.Visibility is { } requested) entity.Visibility = requested;
        if (InvestigationVisibilityFilter.Reject(entity.Visibility, placement.Place) is { } scopeError)
            return BadRequest(scopeError);

        await db.SaveChangesAsync(ct);
        var loaded = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstAsync(i => i.Id == entity.Id, ct);

        // True by construction rather than by another query: this endpoint already refused
        // everyone who cannot manage it, several lines above.
        return Ok(_mapper.Map<InvestigationRecord>(loaded) with { CanEditRecord = true });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var entity = await db.Investigations.FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        if (entity is null) return NotFound();
        if (!await CanManageAsync(id, ct)) return Forbid();


        // Detach the binder entries first. The FK is NoAction (SQL Server won't allow SetNull —
        // see BenDataContext), so without this the delete fails on a referential constraint. The
        // notes and readings survive on the case timeline; the visit they were taken during is what
        // is being removed, not the findings.
        var binderEntries = await db.CaseTimelineEntries
            .Where(e => e.InvestigationId == id)
            .ToListAsync(ct);
        foreach (var e in binderEntries) e.InvestigationId = null;

        db.Investigations.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Org cancels a scheduled investigation and notifies the client via CaseMessage.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var investigation = await db.Investigations.FirstOrDefaultAsync(i => i.Id == id && i.CaseId == caseId, ct);
        if (investigation is null) return NotFound();

        // Cancelling tells the client the visit is off, so it is a management act rather than a
        // member one — same gate as editing, applied after the row is known to exist.
        if (!await CanManageAsync(id, ct)) return Forbid();

        if (investigation.Status != InvestigationStatus.Scheduled)
            return Conflict($"Investigation is already {investigation.Status}.");

        investigation.Status = InvestigationStatus.Cancelled;
        investigation.DateUpdated = DateTime.UtcNow;
        investigation.UpdatedByAppUserId = userId;

        // Notify the client via the case message board
        db.CaseMessages.Add(new Ben.Data.Source.Entities.CaseMessage
        {
            Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = userId,
            Body = $"The investigation scheduled for <strong>{investigation.ScheduledDateTime.ToLocalTime():MMM d, yyyy h:mm tt}</strong> has been cancelled by the organisation.",
            SenderSide = Ben.Data.Common.Enums.CaseMessageSide.Organization,
            IsReadByClient = false, IsReadByOrg = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
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
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
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
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (!await db.Investigations.AnyAsync(i => i.Id == id && i.CaseId == caseId, ct))
            return NotFound();
        if (!await CanManageAsync(id, ct)) return Forbid();

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
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var attendee = await db.InvestigationAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.InvestigationId == id, ct);
        if (attendee is null) return NotFound();

        // Two different rights meet on this one endpoint. Answering your own invitation is yours
        // by definition; saying who turned up, what job they did, or who is leading is managing
        // the visit. So the RSVP branch is self-gated and everything else needs manage.
        var userId = GetCurrentUserId();
        var isSelf = attendee.AppUserId == userId;
        var canManage = await InvestigationAccess.CanManageAsync(
            db, id, userId, User.IsInRole(RoleNames.SuperAdmin), ct);

        if (request.Rsvp.HasValue)
        {
            if (!isSelf && !canManage) return Forbid();
            attendee.Rsvp = request.Rsvp.Value;
        }

        var changesSomeoneElsesRecord =
            request.DidAttend.HasValue
            || request.AssignedRole is not null
            || request.IsLead.HasValue;

        if (changesSomeoneElsesRecord)
        {
            // Deliberately not "unless it's your own row": marking yourself as having attended, or
            // making yourself the lead, is precisely what this must not allow.
            if (!canManage) return Forbid();

            if (request.DidAttend.HasValue) attendee.DidAttend = request.DidAttend;
            attendee.AssignedRole = request.AssignedRole?.Trim() ?? attendee.AssignedRole;
            if (request.IsLead.HasValue) attendee.IsLead = request.IsLead.Value;
        }

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
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var attendee = await db.InvestigationAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.InvestigationId == id, ct);
        if (attendee is null) return NotFound();
        if (!await CanManageAsync(id, ct)) return Forbid();

        db.InvestigationAttendees.Remove(attendee);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);
    }

    /// <summary>
    /// Whether the caller may change this particular investigation. See
    /// <see cref="InvestigationAccess"/> for the five ways to earn it and why membership alone is
    /// no longer one of them.
    /// </summary>
    private async Task<bool> CanManageAsync(Guid investigationId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return await InvestigationAccess.CanManageAsync(
            db, investigationId, GetCurrentUserId(), User.IsInRole(RoleNames.SuperAdmin), ct);
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
            CurrentUserVote:   myVote,
            Score:             EvidenceVoteScore.Score(votes.Select(v => v.VoteType))));
    }

    /// <summary>Returns all votes with voter identities. Requires org membership (any active
    /// organization — this route has no case/org context of its own to scope more narrowly), not
    /// just authentication; the summary endpoint above is the public-facing (counts-only) view.</summary>
    [HttpGet("{uploadFileId:guid}")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<EvidenceVoteRecord>>> GetAll(
        Guid uploadFileId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var isOrgMember = User.IsInRole(RoleNames.SuperAdmin)
            || await db.OrganizationUserMemberships.AnyAsync(m => m.AppUserId == userId && m.IsActive, ct);
        if (!isOrgMember) return Forbid();

        var votes = await db.EvidenceVotes.AsNoTracking()
            .Include(v => v.VoterAppUser)
            .Include(v => v.VoterOrganization)
            .Include(v => v.Case)
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

        // Determine voter's org membership
        bool isPublic = !await db.OrganizationUserMemberships
            .AnyAsync(m => m.AppUserId == userId && m.IsActive, ct);

        Guid? voterOrgId = isPublic ? null : await db.OrganizationUserMemberships
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => (Guid?)m.OrganizationId)
            .FirstOrDefaultAsync(ct);

        // Compute vote context fields
        var uploadFile = await db.UploadFiles.AsNoTracking()
            .FirstAsync(f => f.Id == uploadFileId, ct);
        bool isOriginalUploader = uploadFile.AppUserId == userId;

        var caseEntry = await db.CaseTimelineEntryFiles.AsNoTracking()
            .Include(f => f.CaseTimelineEntry).ThenInclude(e => e.Case).ThenInclude(c => c.ClientRequest)
            .FirstOrDefaultAsync(f => f.UploadFileId == uploadFileId
                && f.CaseTimelineEntry.EntryType == Ben.Data.Common.Enums.CaseTimelineEntryType.Evidence, ct);

        Guid? caseId                 = caseEntry?.CaseTimelineEntry.CaseId;
        Guid? caseOrgId              = caseEntry?.CaseTimelineEntry.Case.OrganizationId;
        Guid? caseClientId           = caseEntry?.CaseTimelineEntry.Case.ClientRequest?.AppUserId;
        bool isVoterCaseOrgMember    = caseOrgId.HasValue && voterOrgId == caseOrgId;
        bool isVoterCaseClient       = caseClientId.HasValue && caseClientId == userId;

        string? voterOrgName = voterOrgId.HasValue
            ? await db.Organizations.Where(o => o.Id == voterOrgId).Select(o => o.Name).FirstOrDefaultAsync(ct)
            : null;

        var existing = await db.EvidenceVotes
            .FirstOrDefaultAsync(v => v.UploadFileId == uploadFileId && v.VoterAppUserId == userId, ct);

        if (existing is not null)
        {
            existing.VoteType              = request.VoteType;
            existing.Comment               = request.Comment?.Trim();
            existing.DateVoted             = DateTime.UtcNow;
            // Re-compute context on update in case membership changed
            existing.IsOriginalUploader    = isOriginalUploader;
            existing.CaseId                = caseId;
            existing.IsVoterCaseOrgMember  = isVoterCaseOrgMember;
            existing.IsVoterCaseClient     = isVoterCaseClient;
            existing.VoterOrganizationName = voterOrgName;
        }
        else
        {
            db.EvidenceVotes.Add(new EvidenceVote
            {
                Id = Guid.NewGuid(), UploadFileId = uploadFileId, VoterAppUserId = userId,
                VoterOrganizationId    = voterOrgId,
                VoterOrganizationName  = voterOrgName,
                VoteType               = request.VoteType,
                Comment                = request.Comment?.Trim(),
                IsPublicVoter          = isPublic,
                IsOriginalUploader     = isOriginalUploader,
                CaseId                 = caseId,
                IsVoterCaseOrgMember   = isVoterCaseOrgMember,
                IsVoterCaseClient      = isVoterCaseClient,
                DateVoted              = DateTime.UtcNow,
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
    DateTime? EvidenceDueDate = null,
    // Both optional and mutually exclusive in practice: name an existing place, or describe a new
    // one. Neither given, a case-bound visit falls back to the case's own place.
    Guid? PlaceId = null,
    NewPlaceRequest? NewPlace = null,
    // Null means "leave it alone": defaulted from the place on create, untouched on update.
    Ben.Data.Common.Enums.InvestigationVisibility? Visibility = null);

public sealed record AddInvestigationAttendeeRequest(Guid AppUserId, string? AssignedRole);
/// <summary>
/// Changes one attendee's row. Every field is optional and each is gated separately — see
/// <c>UpdateAttendance</c>: <see cref="Rsvp"/> is yours to set, the rest belong to whoever manages
/// the visit.
/// </summary>
/// <remarks>
/// <see cref="DidAttend"/> became nullable-meaning-unchanged rather than nullable-meaning-unknown.
/// It was previously assigned unconditionally, so a request that only meant to set an RSVP silently
/// wiped whether the person had turned up.
/// </remarks>
public sealed record UpdateAttendanceRequest(
    bool? DidAttend,
    string? AssignedRole,
    Ben.Data.Common.Enums.RsvpStatus? Rsvp = null,
    bool? IsLead = null);
public sealed record CastEvidenceVoteRequest(
    Ben.Data.Common.Enums.EvidenceVoteType VoteType,
    string? Comment);
