using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organizations/{orgId:guid}/membership-requests")]
[Authorize]
public sealed class OrganizationMembershipRequestController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;
    private readonly IOrganizationSecurityService _security;
    private readonly IAuditLogService _auditLog;

    public OrganizationMembershipRequestController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
    {
        _dbFactory = dbFactory;
        _mapper    = mapper;
        _security  = security;
        _auditLog  = auditLog;
    }

    private Guid? CurrentUserId()
    {
        var appUserIdClaim = User.FindFirst("app_user_id")?.Value;
        if (appUserIdClaim is not null && Guid.TryParse(appUserIdClaim, out var id1)) return id1;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return sub is not null && Guid.TryParse(sub, out var id2) ? id2 : null;
    }

    // ── GET /api/organizations/{orgId}/membership-requests ───────────────────
    /// <summary>
    /// Returns all membership requests for the organization.
    /// Requires MembershipRequests-Read permission or SuperAdmin.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationMembershipRequestRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canRead = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Read, ct);
            if (!canRead) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var requests = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .Include(r => r.Applicant)
            .Include(r => r.UpdatedByAppUser)
            .Where(r => r.OrganizationId == orgId)
            .OrderByDescending(r => r.DateCreated)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<OrganizationMembershipRequestRecord>>(requests));
    }

    // ── GET /api/organizations/{orgId}/membership-requests/my ───────────────
    /// <summary>Returns the current user's membership request for this organization, if any.</summary>
    [HttpGet("my")]
    public async Task<ActionResult<OrganizationMembershipRequestRecord?>> GetMine(
        Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .Include(r => r.Applicant)
            .Include(r => r.UpdatedByAppUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrganizationId == orgId && r.AppUserId == userId.Value, ct);

        if (request is null) return NotFound();
        return Ok(_mapper.Map<OrganizationMembershipRequestRecord>(request));
    }

    // ── POST /api/organizations/{orgId}/membership-requests ─────────────────
    /// <summary>
    /// Submits a membership application. The organization must have IsAcceptingApplications = true.
    /// A user can only have one active (Pending) request per organization.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrganizationMembershipRequestRecord>> Apply(
        Guid orgId, [FromBody] ApplyForMembershipRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound("Organization not found.");
        if (!org.IsAcceptingApplications) return BadRequest("This organization is not currently accepting membership applications.");

        // Prevent duplicate active requests
        var existing = await db.OrganizationMembershipRequests
            .AnyAsync(r => r.OrganizationId == orgId && r.AppUserId == userId.Value
                        && r.Status == OrganizationMembershipRequestStatus.Pending, ct);
        if (existing) return Conflict("You already have a pending application for this organization.");

        // Prevent applying if already a member
        var isMember = await db.OrganizationUserMemberships
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId.Value && m.IsActive, ct);
        if (isMember) return Conflict("You are already a member of this organization.");

        var membershipRequest = new OrganizationMembershipRequest
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            AppUserId          = userId.Value,
            RequestMessage     = request.Message?.Trim(),
            Status             = OrganizationMembershipRequestStatus.Pending,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };

        db.OrganizationMembershipRequests.Add(membershipRequest);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationMembershipRequest), membershipRequest.Id, membershipRequest, membershipRequest.AppUserId, AppSources.WebApi, ct));

        var created = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .Include(r => r.Applicant)
            .Include(r => r.UpdatedByAppUser)
            .AsNoTracking()
            .FirstAsync(r => r.Id == membershipRequest.Id, ct);

        return CreatedAtAction(nameof(GetMine), new { orgId },
            _mapper.Map<OrganizationMembershipRequestRecord>(created));
    }

    // ── PUT /api/organizations/{orgId}/membership-requests/{id}/respond ──────
    /// <summary>
    /// Accepts or denies a pending membership application.
    /// Requires MembershipRequests-Update permission or SuperAdmin.
    /// On acceptance the applicant is added as a Member.
    /// Either way a UserMessage notification is sent to the applicant.
    /// </summary>
    [HttpPut("{id:guid}/respond")]
    public async Task<ActionResult<OrganizationMembershipRequestRecord>> Respond(
        Guid orgId, Guid id,
        [FromBody] RespondToMembershipRequest request,
        CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canUpdate = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Update, ct);
            if (!canUpdate) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var before = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct);

        var membershipRequest = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct);

        if (membershipRequest is null) return NotFound();
        if (membershipRequest.Status != OrganizationMembershipRequestStatus.Pending)
            return Conflict("This request has already been responded to.");

        var accepted = request.Status == OrganizationMembershipRequestStatus.Accepted;
        var denied   = request.Status == OrganizationMembershipRequestStatus.Denied;

        if (!accepted && !denied)
            return BadRequest("Status must be Accepted or Denied.");

        membershipRequest.Status             = request.Status;
        membershipRequest.DateUpdated        = DateTime.UtcNow;
        membershipRequest.UpdatedByAppUserId = userId.Value;

        // Phase 3: persist denial metadata
        if (denied)
        {
            membershipRequest.CanReapply    = request.CanReapply;
            membershipRequest.DenialReason  = request.DenialReason?.Trim();
            membershipRequest.IsUnderReview = false;
        }

        // If accepted, create the membership
        if (accepted)
        {
            var alreadyMember = await db.OrganizationUserMemberships
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == membershipRequest.AppUserId && m.IsActive, ct);

            if (!alreadyMember)
            {
                db.OrganizationUserMemberships.Add(new OrganizationUserMembership
                {
                    Id                 = Guid.NewGuid(),
                    OrganizationId     = orgId,
                    AppUserId          = membershipRequest.AppUserId,
                    Role               = OrganizationMemberRole.Member,
                    IsActive           = true,
                    DateCreated        = DateTime.UtcNow,
                    CreatedByAppUserId = userId.Value,
                });
            }
        }

        // Send a UserMessage notification to the applicant
        var orgName = membershipRequest.Organization.Name;
        var subject = accepted
            ? $"Membership Accepted: {orgName}"
            : $"Membership Application Update: {orgName}";
        var body = accepted
            ? $"Your application to join <strong>{orgName}</strong> has been accepted. Welcome to the organization!"
            : $"Your application to join <strong>{orgName}</strong> has not been approved at this time. " +
              (string.IsNullOrWhiteSpace(request.ResponseNote)
                  ? string.Empty
                  : $"<br><br><em>{request.ResponseNote.Trim()}</em>");

        var message = new UserMessage
        {
            Id                 = Guid.NewGuid(),
            UserMessageTypeId  = OrganizationSeeder.MembershipResponseMessageTypeId,
            MessageSubject     = subject,
            MessageBody        = body,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };
        db.UserMessages.Add(message);
        db.UserMessageTos.Add(new UserMessageTo
        {
            Id          = Guid.NewGuid(),
            MessageId   = message.Id,
            ToAppUserId = membershipRequest.AppUserId,
        });

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationMembershipRequest), id, before!, membershipRequest, userId.Value, AppSources.WebApi, ct));

        var updated = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .Include(r => r.Applicant)
            .Include(r => r.UpdatedByAppUser)
            .AsNoTracking()
            .FirstAsync(r => r.Id == id, ct);

        return Ok(_mapper.Map<OrganizationMembershipRequestRecord>(updated));
    }

    // ── DELETE /api/organizations/{orgId}/membership-requests/{id} ──────────
    /// <summary>
    /// Withdraws a pending membership application. Only the applicant themselves may withdraw.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Withdraw(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var before = await db.OrganizationMembershipRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct);

        var request = await db.OrganizationMembershipRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct);

        if (request is null) return NotFound();
        if (request.AppUserId != userId.Value && !User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();
        if (request.Status != OrganizationMembershipRequestStatus.Pending)
            return Conflict("Only pending requests can be withdrawn.");

        request.Status             = OrganizationMembershipRequestStatus.Withdrawn;
        request.DateUpdated        = DateTime.UtcNow;
        request.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationMembershipRequest), id, before!, request, userId.Value, AppSources.WebApi, ct));
        return NoContent();
    }

    /// <summary>
    /// Escalates a pending application to committee vote with a deadline.
    /// When the deadline passes, votes are tallied and the application is
    /// auto-resolved: majority Approve → Accepted; otherwise → Denied.
    /// </summary>
    [HttpPost("{id:guid}/open-vote")]
    public async Task<ActionResult<OrganizationMembershipRequestRecord>> OpenVote(
        Guid orgId, Guid id, [FromBody] OpenVoteRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Update, ct);
            if (!ok) return Forbid();
        }
        if (request.VoteDeadline <= DateTime.UtcNow)
            return BadRequest("Vote deadline must be in the future.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var req = await db.OrganizationMembershipRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct);
        if (req is null) return NotFound();
        if (req.Status != OrganizationMembershipRequestStatus.Pending)
            return BadRequest("Only pending requests can be opened for a vote.");

        req.IsUnderReview      = true;
        req.VoteDeadline       = request.VoteDeadline;
        req.DateUpdated        = DateTime.UtcNow;
        req.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok(_mapper.Map<OrganizationMembershipRequestRecord>(req));
    }

    /// <summary>Cast or update a vote on an application under review.</summary>
    [HttpPost("{id:guid}/vote")]
    public async Task<ActionResult<MembershipReviewVoteRecord>> CastVote(
        Guid orgId, Guid id, [FromBody] CastVoteRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Update, ct);
            if (!ok) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var req = await db.OrganizationMembershipRequests
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId, ct);
        if (req is null) return NotFound();
        if (!req.IsUnderReview) return BadRequest("This request is not currently under review.");
        if (req.VoteDeadline.HasValue && req.VoteDeadline.Value < DateTime.UtcNow)
            return BadRequest("The vote deadline has passed.");

        // Upsert — one vote per reviewer per request
        var existing = await db.MembershipReviewVotes
            .FirstOrDefaultAsync(v => v.OrganizationMembershipRequestId == id
                                   && v.VoterAppUserId == userId.Value, ct);
        if (existing is not null)
        {
            existing.VoteType  = request.VoteType;
            existing.Comment   = request.Comment?.Trim();
            existing.DateVoted = DateTime.UtcNow;
        }
        else
        {
            var vote = new MembershipReviewVote
            {
                Id = Guid.NewGuid(),
                OrganizationMembershipRequestId = id,
                VoterAppUserId = userId.Value,
                VoteType       = request.VoteType,
                Comment        = request.Comment?.Trim(),
                DateVoted      = DateTime.UtcNow,
            };
            db.MembershipReviewVotes.Add(vote);
            existing = vote;
        }
        await db.SaveChangesAsync(ct);

        var loaded = await db.MembershipReviewVotes.AsNoTracking()
            .Include(v => v.VoterAppUser)
            .FirstAsync(v => v.Id == existing.Id, ct);
        return Ok(_mapper.Map<MembershipReviewVoteRecord>(loaded));
    }

    /// <summary>
    /// Returns all votes cast on an application.
    /// Requires MembershipRequests-Update permission.
    /// </summary>
    [HttpGet("{id:guid}/votes")]
    public async Task<ActionResult<IEnumerable<MembershipReviewVoteRecord>>> GetVotes(
        Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.MembershipRequests, OrganizationSecurityAction.Update, ct);
            if (!ok) return Forbid();
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationMembershipRequests.AsNoTracking()
                .AnyAsync(r => r.Id == id && r.OrganizationId == orgId, ct))
            return NotFound();

        var votes = await db.MembershipReviewVotes
            .AsNoTracking()
            .Include(v => v.VoterAppUser)
            .Where(v => v.OrganizationMembershipRequestId == id)
            .OrderBy(v => v.DateVoted)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<MembershipReviewVoteRecord>>(votes));
    }

    private static async Task TryAuditAsync(Task auditTask)
    {
        try { await auditTask; }
        catch { /* audit failure must not surface to the caller */ }
    }
}

public sealed record ApplyForMembershipRequest(string? Message);
public sealed record RespondToMembershipRequest(
    OrganizationMembershipRequestStatus Status,
    string? ResponseNote,
    bool? CanReapply = null,
    string? DenialReason = null);
public sealed record OpenVoteRequest(DateTime VoteDeadline);
public sealed record CastVoteRequest(Ben.Data.Common.Enums.MembershipVoteType VoteType, string? Comment);
