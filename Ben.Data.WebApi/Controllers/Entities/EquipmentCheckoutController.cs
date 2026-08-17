using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Borrowing equipment: request, approve or deny, hand over, return.
/// </summary>
/// <remarks>
/// <para>One endpoint per transition, each guarding the state it expects and answering
/// <c>409 Conflict</c> when the loan has moved on — the house style every other approval flow here
/// uses (<c>UploadFilePermissionRequestController</c> is the closest sibling, and its own summary
/// is a post-mortem of the mistakes this one avoids).</para>
///
/// <para><b>Identity always comes from the caller's claims, never the request body.</b> Nothing in
/// a payload says who is borrowing, who approved, or who received something back.</para>
///
/// <para>Answers <c>404</c> rather than <c>403</c> when the caller has no business seeing a loan at
/// all, so the endpoint cannot be used to discover which loan ids exist.</para>
/// </remarks>
[ApiController]
[Route("api/equipment-checkouts")]
[Authorize]
public sealed class EquipmentCheckoutController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;
    private readonly IAuditLogService _auditLog;

    public EquipmentCheckoutController(
        IDbContextFactory<BenDataContext> db,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
    {
        _db       = db;
        _security = security;
        _auditLog = auditLog;
    }

    private bool IsSuperAdmin() => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

    /// <summary>Whether the caller may ask to borrow this item, and on whose behalf.</summary>
    [HttpGet("eligibility/{itemId:guid}")]
    public async Task<ActionResult<BorrowEligibilityRecord>> GetEligibility(Guid itemId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();

        return Ok(await EquipmentAccess.ComputeBorrowEligibilityAsync(db, item, userId, ct));
    }

    // ── The state machine ────────────────────────────────────────────────────

    /// <summary>Asks to borrow a piece of equipment.</summary>
    /// <remarks>
    /// Re-validates eligibility server-side rather than trusting the form's own option list, and
    /// checks the chosen borrowing group is one the eligibility answer actually offered — a client
    /// cannot invent a group to borrow on behalf of.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<EquipmentCheckoutRecord>> RequestCheckout(
        [FromBody] RequestEquipmentCheckoutRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.EquipmentItemId, ct);
        if (item is null) return NotFound();

        var eligibility = await EquipmentAccess.ComputeBorrowEligibilityAsync(db, item, userId, ct);
        if (!eligibility.CanRequest) return BadRequest(eligibility.Reason ?? "You can't borrow this equipment.");

        if (!eligibility.Options.Any(o => o.OrganizationId == request.BorrowedForOrganizationId))
            return BadRequest("You can't borrow this equipment on that group's behalf.");

        // One live ask per person per item: a second is a duplicate, not a queue position.
        var alreadyAsking = await db.EquipmentCheckouts.AnyAsync(c =>
            c.EquipmentItemId == item.Id
            && c.BorrowerAppUserId == userId
            && (c.Status == EquipmentCheckoutStatus.Requested
                || c.Status == EquipmentCheckoutStatus.Approved
                || c.Status == EquipmentCheckoutStatus.CheckedOut), ct);
        if (alreadyAsking) return Conflict("You already have an open request or loan for this equipment.");

        if (request.InvestigationId is Guid investigationId)
        {
            // A visit can only be attached when the loan is actually for that visit's group.
            var investigationOrgId = await db.Investigations.AsNoTracking()
                .Where(i => i.Id == investigationId).Select(i => (Guid?)i.OrganizationId).FirstOrDefaultAsync(ct);
            if (investigationOrgId is null) return BadRequest("That investigation could not be found.");
            if (investigationOrgId != request.BorrowedForOrganizationId)
                return BadRequest("That investigation belongs to a different group than this loan.");
        }

        var entity = new EquipmentCheckout
        {
            Id                        = Guid.NewGuid(),
            EquipmentItemId           = item.Id,
            BorrowerAppUserId         = userId,
            BorrowedForOrganizationId = request.BorrowedForOrganizationId,
            InvestigationId           = request.InvestigationId,
            Status                    = EquipmentCheckoutStatus.Requested,
            RequestNotes              = string.IsNullOrWhiteSpace(request.RequestNotes) ? null : request.RequestNotes.Trim(),
            DateNeededFrom            = request.DateNeededFrom,
            DateCreated               = DateTime.UtcNow,
            CreatedByAppUserId        = userId,
        };
        db.EquipmentCheckouts.Add(entity);

        await NotifyApproversAsync(db, item, entity, userId, ct);

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(EquipmentCheckout), entity.Id, entity, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        return await ProjectOneAsync(db, entity.Id, userId, ct);
    }

    /// <summary>Approves a request, optionally setting a due date.</summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<EquipmentCheckoutRecord>> Approve(
        Guid id, [FromBody] ApproveEquipmentCheckoutRequest request, CancellationToken ct)
        => await TransitionAsync(id, EquipmentCheckoutStatus.Requested, requireApprover: true, ct,
            apply: (db, checkout, userId) =>
            {
                checkout.Status              = EquipmentCheckoutStatus.Approved;
                checkout.DateDue             = request.DateDue;
                checkout.ReviewNotes         = string.IsNullOrWhiteSpace(request.ReviewNotes) ? null : request.ReviewNotes.Trim();
                checkout.ReviewedByAppUserId = userId;
                checkout.DateReviewed        = DateTime.UtcNow;
                NotifyBorrower(db, checkout, userId,
                    "Your equipment request was approved",
                    checkout.DateDue is null
                        ? "Your request to borrow equipment has been approved. Confirm the hand-off once you have it."
                        : $"Your request to borrow equipment has been approved. It's due back on {checkout.DateDue:d MMM yyyy}.");
            });

    /// <summary>Turns a request down. A reason is required — a bare refusal helps nobody.</summary>
    [HttpPost("{id:guid}/deny")]
    public async Task<ActionResult<EquipmentCheckoutRecord>> Deny(
        Guid id, [FromBody] DenyEquipmentCheckoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewNotes))
            return BadRequest("Please give a reason when turning a request down.");

        return await TransitionAsync(id, EquipmentCheckoutStatus.Requested, requireApprover: true, ct,
            apply: (db, checkout, userId) =>
            {
                checkout.Status              = EquipmentCheckoutStatus.Denied;
                checkout.ReviewNotes         = request.ReviewNotes.Trim();
                checkout.ReviewedByAppUserId = userId;
                checkout.DateReviewed        = DateTime.UtcNow;
                NotifyBorrower(db, checkout, userId,
                    "Your equipment request was declined",
                    $"Your request to borrow equipment was declined. Reason given: {checkout.ReviewNotes}");
            });
    }

    /// <summary>The borrower pulls out, before they have the gear.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<EquipmentCheckoutRecord>> Cancel(Guid id, CancellationToken ct)
        => await TransitionAsync(id, null, requireApprover: false, ct,
            requireBorrower: true,
            allowedFrom: [EquipmentCheckoutStatus.Requested, EquipmentCheckoutStatus.Approved],
            apply: (db, checkout, userId) => checkout.Status = EquipmentCheckoutStatus.Cancelled);

    /// <summary>
    /// The borrower confirms the gear is now in their hands.
    /// </summary>
    /// <remarks>
    /// Deliberately the borrower's action, not the lender's: the person who now has the equipment
    /// is the one who can truthfully say so.
    /// </remarks>
    [HttpPost("{id:guid}/confirm-handoff")]
    public async Task<ActionResult<EquipmentCheckoutRecord>> ConfirmHandoff(Guid id, CancellationToken ct)
        => await TransitionAsync(id, EquipmentCheckoutStatus.Approved, requireApprover: false, ct,
            requireBorrower: true,
            apply: (db, checkout, userId) =>
            {
                checkout.Status                         = EquipmentCheckoutStatus.CheckedOut;
                checkout.DateCheckedOut                 = DateTime.UtcNow;
                checkout.CheckedOutConfirmedByAppUserId = userId;
            });

    /// <summary>
    /// The lender confirms the gear came back.
    /// </summary>
    /// <remarks>
    /// The mirror of the hand-off: each side attests to the transfer coming toward them, so a
    /// borrower cannot close a loan by asserting they returned something.
    /// </remarks>
    [HttpPost("{id:guid}/return")]
    public async Task<ActionResult<EquipmentCheckoutRecord>> Return(
        Guid id, [FromBody] ReturnEquipmentCheckoutRequest request, CancellationToken ct)
        => await TransitionAsync(id, EquipmentCheckoutStatus.CheckedOut, requireApprover: true, ct,
            apply: (db, checkout, userId) =>
            {
                checkout.Status                      = EquipmentCheckoutStatus.Returned;
                checkout.DateReturned                = DateTime.UtcNow;
                checkout.ReturnedReceivedByAppUserId = userId;
                checkout.ReturnConditionNotes        = string.IsNullOrWhiteSpace(request.ReturnConditionNotes)
                    ? null : request.ReturnConditionNotes.Trim();
                NotifyBorrower(db, checkout, userId,
                    "Equipment return recorded",
                    "The equipment you borrowed has been recorded as returned. Thanks.");
            });

    /// <summary>
    /// The shared body of every transition: load, authorize, guard the current state, apply, save,
    /// audit, project.
    /// </summary>
    /// <remarks>
    /// One place so that no transition can quietly skip a check. A wrong current state is a
    /// <c>409</c> — the loan moved on, which is a conflict rather than a bad request — while not
    /// being entitled to touch this loan at all is a <c>404</c>.
    /// </remarks>
    private async Task<ActionResult<EquipmentCheckoutRecord>> TransitionAsync(
        Guid id,
        EquipmentCheckoutStatus? requiredStatus,
        bool requireApprover,
        CancellationToken ct,
        Action<BenDataContext, EquipmentCheckout, Guid> apply,
        bool requireBorrower = false,
        IReadOnlyList<EquipmentCheckoutStatus>? allowedFrom = null)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var checkout = await db.EquipmentCheckouts
            .Include(c => c.EquipmentItem)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (checkout is null) return NotFound();

        var isApprover = await EquipmentAccess.CanReviewCheckoutAsync(
            _security, checkout.EquipmentItem, userId, IsSuperAdmin(), ct);
        var isBorrower = checkout.BorrowerAppUserId == userId;

        // Anyone who is neither party has no business knowing this loan exists.
        if (!isApprover && !isBorrower) return NotFound();
        if (requireApprover && !isApprover) return Forbid();
        if (requireBorrower && !isBorrower) return Forbid();

        var statusOk = allowedFrom is not null
            ? allowedFrom.Contains(checkout.Status)
            : requiredStatus is null || checkout.Status == requiredStatus;
        if (!statusOk)
            return Conflict($"This request is already {checkout.Status.ToString().ToLowerInvariant()}.");

        var before = new
        {
            checkout.Status, checkout.DateDue, checkout.ReviewNotes,
            checkout.DateCheckedOut, checkout.DateReturned,
        };

        apply(db, checkout, userId);
        checkout.DateUpdated        = DateTime.UtcNow;
        checkout.UpdatedByAppUserId = userId;

        // Group gear tracks its current holder alongside the loan.
        if (checkout.EquipmentItem.OwningOrganizationId is not null)
        {
            if (checkout.Status == EquipmentCheckoutStatus.CheckedOut)
                checkout.EquipmentItem.CurrentHolderAppUserId = checkout.BorrowerAppUserId;
            else if (checkout.Status == EquipmentCheckoutStatus.Returned
                     && checkout.EquipmentItem.CurrentHolderAppUserId == checkout.BorrowerAppUserId)
                checkout.EquipmentItem.CurrentHolderAppUserId = null;
        }

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(EquipmentCheckout), checkout.Id, before, checkout, userId, Ben.Data.Common.Constants.AppSources.WebApi));

        return await ProjectOneAsync(db, checkout.Id, userId, ct);
    }

    // ── Queues ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The caller's own loans — what they are borrowing, and what they have to decide on.
    /// </summary>
    [HttpGet("/api/me/equipment-checkouts")]
    public async Task<ActionResult<IEnumerable<EquipmentCheckoutRecord>>> GetMine(
        [FromQuery] string role = "borrower", CancellationToken ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        List<EquipmentCheckout> checkouts;
        if (string.Equals(role, "approver", StringComparison.OrdinalIgnoreCase))
        {
            var approverOrgIds = await ApproverOrgIdsAsync(db, userId, ct);

            checkouts = await db.EquipmentCheckouts.AsNoTracking()
                .Include(c => c.EquipmentItem).ThenInclude(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
                .Where(c => c.EquipmentItem.OwnerAppUserId == userId
                         || (c.EquipmentItem.OwningOrganizationId != null
                             && approverOrgIds.Contains(c.EquipmentItem.OwningOrganizationId.Value)))
                .OrderBy(c => c.Status).ThenByDescending(c => c.DateCreated)
                .ToListAsync(ct);
        }
        else
        {
            checkouts = await db.EquipmentCheckouts.AsNoTracking()
                .Include(c => c.EquipmentItem).ThenInclude(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
                .Where(c => c.BorrowerAppUserId == userId)
                .OrderBy(c => c.Status).ThenByDescending(c => c.DateCreated)
                .ToListAsync(ct);
        }

        return Ok(await ProjectManyAsync(db, checkouts, userId, ct));
    }

    /// <summary>One group's loan queue. Needs the EquipmentCheckout permission.</summary>
    [HttpGet("/api/organizations/{orgId:guid}/equipment-checkouts")]
    public async Task<ActionResult<IEnumerable<EquipmentCheckoutRecord>>> GetForOrg(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var canReview = IsSuperAdmin() || await _security.HasAccessAsync(
            userId, orgId, OrganizationSecurityTable.EquipmentCheckout, OrganizationSecurityAction.Read, ct);
        if (!canReview) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var checkouts = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.EquipmentItem).ThenInclude(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Where(c => c.EquipmentItem.OwningOrganizationId == orgId || c.BorrowedForOrganizationId == orgId)
            .OrderBy(c => c.Status).ThenByDescending(c => c.DateCreated)
            .ToListAsync(ct);

        return Ok(await ProjectManyAsync(db, checkouts, userId, ct));
    }

    /// <summary>One item's loan history.</summary>
    [HttpGet("/api/equipment/{itemId:guid}/checkouts")]
    public async Task<ActionResult<IEnumerable<EquipmentCheckoutRecord>>> GetForItem(Guid itemId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();

        var canSee = await EquipmentAccess.CanReviewCheckoutAsync(_security, item, userId, IsSuperAdmin(), ct);
        if (!canSee && item.OwningOrganizationId is Guid orgId)
            canSee = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
        if (!canSee) return NotFound();

        var checkouts = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.EquipmentItem).ThenInclude(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Where(c => c.EquipmentItemId == itemId)
            .OrderByDescending(c => c.DateCreated)
            .ToListAsync(ct);

        return Ok(await ProjectManyAsync(db, checkouts, userId, ct));
    }

    // ── Notifications ────────────────────────────────────────────────────────

    /// <summary>
    /// Tells whoever can approve this that somebody is asking.
    /// </summary>
    /// <remarks>
    /// Rows are added to the caller's own change set, not saved separately, so the notice and the
    /// request it announces commit together or not at all — the pattern
    /// <c>OrgExperienceTypeController.NotifyAppAdministratorsAsync</c> established.
    /// </remarks>
    private async Task NotifyApproversAsync(
        BenDataContext db, EquipmentItem item, EquipmentCheckout checkout, Guid requesterId, CancellationToken ct)
    {
        var recipients = new List<Guid>();

        if (item.OwnerAppUserId is Guid ownerId)
        {
            recipients.Add(ownerId);
        }
        else if (item.OwningOrganizationId is Guid orgId)
        {
            // Everyone whose role or grant carries the checkout permission, plus owners/admins.
            var memberIds = await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.OrganizationId == orgId && m.IsActive)
                .Select(m => m.AppUserId)
                .ToListAsync(ct);

            foreach (var memberId in memberIds)
            {
                if (await _security.HasAccessAsync(memberId, orgId,
                        OrganizationSecurityTable.EquipmentCheckout, OrganizationSecurityAction.Update, ct))
                    recipients.Add(memberId);
            }
        }

        recipients = [.. recipients.Distinct().Where(r => r != requesterId)];
        if (recipients.Count == 0) return;

        var requesterName = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == requesterId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Someone";

        AddMessage(db, recipients, requesterId,
            "Equipment borrowing request",
            $"{requesterName} has asked to borrow {item.DisplayName}. It's waiting for your decision.");
    }

    private static void NotifyBorrower(
        BenDataContext db, EquipmentCheckout checkout, Guid actingUserId, string subject, string body)
    {
        if (checkout.BorrowerAppUserId == actingUserId) return;
        AddMessage(db, [checkout.BorrowerAppUserId], actingUserId, subject, body);
    }

    private static void AddMessage(
        BenDataContext db, IReadOnlyList<Guid> recipientIds, Guid fromUserId, string subject, string body)
    {
        var message = new UserMessage
        {
            Id                 = Guid.NewGuid(),
            UserMessageTypeId  = OrganizationSeeder.EquipmentCheckoutMessageTypeId,
            MessageSubject     = subject,
            MessageBody        = body,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = fromUserId,
        };
        db.UserMessages.Add(message);

        foreach (var recipientId in recipientIds)
        {
            db.UserMessageTos.Add(new UserMessageTo
            {
                Id          = Guid.NewGuid(),
                MessageId   = message.Id,
                ToAppUserId = recipientId,
            });
        }
    }

    // ── Projection ───────────────────────────────────────────────────────────

    /// <summary>Groups whose equipment-checkout queue this caller may act on.</summary>
    private async Task<List<Guid>> ApproverOrgIdsAsync(BenDataContext db, Guid userId, CancellationToken ct)
    {
        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        var approver = new List<Guid>();
        foreach (var orgId in orgIds)
        {
            if (await _security.HasAccessAsync(userId, orgId,
                    OrganizationSecurityTable.EquipmentCheckout, OrganizationSecurityAction.Update, ct))
                approver.Add(orgId);
        }
        return approver;
    }

    private async Task<ActionResult<EquipmentCheckoutRecord>> ProjectOneAsync(
        BenDataContext db, Guid checkoutId, Guid userId, CancellationToken ct)
    {
        var checkout = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.EquipmentItem).ThenInclude(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .FirstAsync(c => c.Id == checkoutId, ct);

        var projected = await ProjectManyAsync(db, [checkout], userId, ct);
        return Ok(projected[0]);
    }

    /// <summary>
    /// Projects loans with their per-viewer flags, resolving each item's approver question once per
    /// distinct item rather than once per row.
    /// </summary>
    private async Task<List<EquipmentCheckoutRecord>> ProjectManyAsync(
        BenDataContext db, IReadOnlyList<EquipmentCheckout> checkouts, Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var isSuperAdmin = IsSuperAdmin();

        var approverByItem = new Dictionary<Guid, bool>();
        foreach (var item in checkouts.Select(c => c.EquipmentItem).DistinctBy(i => i.Id))
            approverByItem[item.Id] = await EquipmentAccess.CanReviewCheckoutAsync(_security, item, userId, isSuperAdmin, ct);

        var userIds = checkouts.SelectMany(c => new[] { (Guid?)c.BorrowerAppUserId, c.ReviewedByAppUserId, c.EquipmentItem.OwnerAppUserId })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await db.AppUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var orgIds = checkouts.Select(c => c.BorrowedForOrganizationId).Where(o => o is not null).Select(o => o!.Value).Distinct().ToList();
        var orgNames = await db.Organizations.AsNoTracking()
            .Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var investigationIds = checkouts.Select(c => c.InvestigationId).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        var investigationTitles = await db.Investigations.AsNoTracking()
            .Where(i => investigationIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Title, ct);

        string? Name(Guid? id) => id is not null && names.TryGetValue(id.Value, out var n) ? n : null;

        return [.. checkouts.Select(c => new EquipmentCheckoutRecord(
            c.Id,
            c.EquipmentItemId,
            c.EquipmentItem.DisplayName,
            c.EquipmentItem.EquipmentModel.EquipmentBrand.Name,
            c.EquipmentItem.EquipmentModel.Name,
            c.EquipmentItem.OwnerAppUserId,
            Name(c.EquipmentItem.OwnerAppUserId),
            c.EquipmentItem.OwningOrganizationId,
            c.BorrowerAppUserId,
            Name(c.BorrowerAppUserId),
            c.BorrowedForOrganizationId,
            c.BorrowedForOrganizationId is not null && orgNames.TryGetValue(c.BorrowedForOrganizationId.Value, out var on) ? on : null,
            c.InvestigationId,
            c.InvestigationId is not null && investigationTitles.TryGetValue(c.InvestigationId.Value, out var it) ? it : null,
            c.Status,
            EquipmentAccess.IsOverdue(c, now),
            c.RequestNotes,
            c.ReviewNotes,
            c.ReviewedByAppUserId,
            Name(c.ReviewedByAppUserId),
            c.DateReviewed,
            c.DateNeededFrom,
            c.DateDue,
            c.DateCheckedOut,
            c.DateReturned,
            c.ReturnConditionNotes,
            c.DateCreated,
            EquipmentAccess.ComputeCheckoutFlags(c, userId, approverByItem.GetValueOrDefault(c.EquipmentItemId))))];
    }
}
