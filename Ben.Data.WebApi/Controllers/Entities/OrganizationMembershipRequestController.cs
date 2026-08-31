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
        // A person can have HISTORY here — withdrawn or denied applications alongside a live
        // one. The row that matters is the Pending one when it exists, else the most recent:
        // an unordered FirstOrDefault returned an arbitrary row, which once handed a caller
        // the old Withdrawn application while a Pending one sat unanswered (item 174).
        var request = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .Include(r => r.Applicant)
            .Include(r => r.UpdatedByAppUser)
            .AsNoTracking()
            .Where(r => r.OrganizationId == orgId && r.AppUserId == userId.Value)
            .OrderByDescending(r => r.Status == OrganizationMembershipRequestStatus.Pending)
            .ThenByDescending(r => r.DateCreated)
            .FirstOrDefaultAsync(ct);

        if (request is null) return NotFound();
        return Ok(_mapper.Map<OrganizationMembershipRequestRecord>(request));
    }

    // ── GET /api/me/membership-requests ─────────────────────────────────────
    /// <summary>
    /// Every application this person has made, across all organizations.
    /// </summary>
    /// <remarks>
    /// <para><b>IH-04, Ben's 2026-08-26 production sweep.</b> An applicant had nowhere to see
    /// that their own application existed. The per-organization <c>my</c> endpoint above only
    /// answers for somebody who already knows to go and look at that group's page — which an
    /// applicant, by definition, is not a member of. So a person applied, saw no acknowledgement
    /// anywhere in their account, and reasonably concluded it had not gone through. One test
    /// account accumulated <b>23 applications to the same group</b>.</para>
    ///
    /// <para>Deliberately account-scoped rather than org-scoped, and it returns resolved
    /// applications too: "you were declined" is also an answer somebody is owed.</para>
    /// </remarks>
    [HttpGet("/api/me/membership-requests")]
    public async Task<ActionResult<IEnumerable<OrganizationMembershipRequestRecord>>> GetMineEverywhere(
        CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var requests = await db.OrganizationMembershipRequests
            .Include(r => r.Organization)
            .Include(r => r.Applicant)
            .Include(r => r.UpdatedByAppUser)
            .AsNoTracking()
            .Where(r => r.AppUserId == userId.Value)
            // Pending first — those are the ones somebody is waiting on — then most recent.
            .OrderByDescending(r => r.Status == OrganizationMembershipRequestStatus.Pending)
            .ThenByDescending(r => r.DateCreated)
            .ToListAsync(ct);

        return Ok(requests.Select(_mapper.Map<OrganizationMembershipRequestRecord>).ToList());
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

        // Deliberately NOT gated on the plan here. The paid gate sits on the ADVERTISING switch,
        // so a free group cannot invite applications in the first place, and on ACCEPTANCE, where
        // the member would actually be added. Refusing the applicant as well would punish the
        // wrong person for a decision that is not theirs — and this door is already closed by
        // IsAcceptingApplications above.

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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost the race against a concurrent Apply for the same (org, user) — the unique
            // filtered index on Pending requests caught what the AnyAsync check above couldn't.
            return Conflict("You already have a pending application for this organization.");
        }
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationMembershipRequest), membershipRequest.Id, membershipRequest, membershipRequest.AppUserId, AppSources.WebApi));

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

            // One person is free; working with other people is the paid part. Guarded here as
            // well as on the advertising switch, because an application can arrive by a route
            // that never reads that flag — an invite link, a direct call.
            if (!alreadyMember
                && await Services.Billing.PaidPlan.WhyCannotAddMemberAsync(db, orgId, ct) is { } needsPlan)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, needsPlan);
            }

            // A personal organization that gains a second person has become a group, and should
            // stop being hidden from the places groups are found. Leaving the flag set would give
            // them a group nobody can discover — the opposite of what they just paid for.
            if (!alreadyMember)
            {
                var joined = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
                if (joined is { IsPersonal: true })
                {
                    joined.IsPersonal = false;
                    joined.DateUpdated = DateTime.UtcNow;
                    joined.UpdatedByAppUserId = userId.Value;
                }
            }

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

        // Item 144: a join past the group's frozen band creates a PendingPayment overflow seat
        // for the NEW member — the group's contract stays at its band, the extra person pays for
        // themselves. Never blocks the join; the seat is the billing record, and the acceptance
        // message carries the price so nobody learns it from an invoice.
        var seat = accepted
            ? await Ben.Data.WebApi.Services.Billing.OverflowSeats.MaybeOfferSeatAsync(
                db, orgId, membershipRequest.AppUserId, userId.Value, ct)
            : null;

        // Send a UserMessage notification to the applicant
        var orgName = membershipRequest.Organization.Name;
        var subject = accepted
            ? $"Membership Accepted: {orgName}"
            : $"Membership Application Update: {orgName}";
        var body = accepted
            ? $"Your application to join <strong>{orgName}</strong> has been accepted. Welcome to the organization!"
              + (seat is null ? string.Empty
                  : $"<br><br><strong>{orgName}</strong> has grown past its plan's member count, so your "
                  + $"seat is billed individually: <strong>${seat.PriceAtStart:0.00} per "
                  + $"{Ben.Data.WebApi.Services.Billing.OverflowSeats.CadenceNoun(seat.Interval)}</strong>. "
                  + "Your membership is active now; you'll find the seat and its status on the Pricing page.")
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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationMembershipRequest), id, before!, membershipRequest, userId.Value, AppSources.WebApi));

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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationMembershipRequest), id, before!, request, userId.Value, AppSources.WebApi));
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
