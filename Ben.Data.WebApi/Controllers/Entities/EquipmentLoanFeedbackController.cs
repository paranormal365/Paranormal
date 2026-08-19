using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// What each side of a finished loan had to say about the other.
/// </summary>
/// <remarks>
/// <para><b>The subject never sees their own feedback.</b> Every read below excludes them
/// structurally, and there are <b>no notifications on this controller at all</b> — telling somebody
/// that feedback about them exists is most of the way to showing it to them.</para>
///
/// <para>The two directions are deliberately asymmetric in attribution. Lender-about-borrower is
/// <b>attributed</b>: it is lender-to-lender context, and an unattributed warning is hard to weigh.
/// Borrower-about-lender is <b>unattributed</b>: a borrower has more to lose by being named. That
/// asymmetry is the only difference between them.</para>
///
/// <para>A borrower may also review the <i>gear</i>, which is the one part of this table that is
/// ever public — it feeds the make/model page, stripped of its author.</para>
/// </remarks>
[ApiController]
[Route("api/equipment")]
public sealed class EquipmentLoanFeedbackController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;

    public EquipmentLoanFeedbackController(IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security)
    {
        _db       = db;
        _security = security;
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>Leaves feedback on a loan that has been returned.</summary>
    /// <remarks>
    /// Only after return: feedback about how somebody treated your gear is not a judgement anyone
    /// can make while they still have it, and letting a lender rate a borrower mid-loan would make
    /// it a lever.
    /// </remarks>
    [HttpPost("checkouts/{checkoutId:guid}/feedback")]
    [Authorize]
    public async Task<IActionResult> SubmitFeedback(
        Guid checkoutId, [FromBody] SubmitLoanFeedbackRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (request.Rating is int rating && rating is < 1 or > 5)
            return BadRequest("A rating is between 1 and 5.");
        if (string.IsNullOrWhiteSpace(request.CounterpartyComment)
            && request.Rating is null
            && string.IsNullOrWhiteSpace(request.ProductComment))
            return BadRequest("There is nothing to save.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var checkout = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.EquipmentItem)
            .FirstOrDefaultAsync(c => c.Id == checkoutId, ct);
        if (checkout is null) return NotFound();

        var role = await ResolveRoleAsync(db, checkout, userId, ct);
        if (role is null) return NotFound();   // not a party — and not told that the loan exists

        if (checkout.Status != EquipmentCheckoutStatus.Returned)
            return Conflict("Feedback can be left once the equipment is back.");

        // A lender reviewing their own gear on its public model page would be an advertisement.
        if (role == EquipmentFeedbackRole.Lender && !string.IsNullOrWhiteSpace(request.ProductComment))
            return BadRequest("Only the borrower can review the equipment itself.");

        if (await db.EquipmentLoanFeedbacks.AnyAsync(
                f => f.EquipmentCheckoutId == checkoutId && f.Role == role, ct))
            return Conflict("You have already left feedback on this loan.");

        var (subjectUserId, subjectOrgId) = ResolveSubject(checkout, role.Value);

        db.EquipmentLoanFeedbacks.Add(new EquipmentLoanFeedback
        {
            Id                    = Guid.NewGuid(),
            EquipmentCheckoutId   = checkoutId,
            AuthorAppUserId       = userId,
            Role                  = role.Value,
            CounterpartyComment   = Trimmed(request.CounterpartyComment),
            Rating                = request.Rating,
            ProductComment        = role == EquipmentFeedbackRole.Borrower ? Trimmed(request.ProductComment) : null,
            SubjectAppUserId      = subjectUserId,
            SubjectOrganizationId = subjectOrgId,
            DateCreated           = DateTime.UtcNow,
            CreatedByAppUserId    = userId,
        });

        // Deliberately no notification. See the class remarks.
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Whether this caller can leave feedback on this loan, and as which side.</summary>
    [HttpGet("checkouts/{checkoutId:guid}/feedback-state")]
    [Authorize]
    public async Task<ActionResult<LoanFeedbackStateRecord>> GetFeedbackState(Guid checkoutId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var checkout = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.EquipmentItem)
            .FirstOrDefaultAsync(c => c.Id == checkoutId, ct);
        if (checkout is null) return NotFound();

        var role = await ResolveRoleAsync(db, checkout, userId, ct);
        if (role is null) return NotFound();

        var already = await db.EquipmentLoanFeedbacks
            .AnyAsync(f => f.EquipmentCheckoutId == checkoutId && f.Role == role, ct);

        return Ok(new LoanFeedbackStateRecord(
            CanLeaveFeedback: checkout.Status == EquipmentCheckoutStatus.Returned && !already,
            AsRole: role,
            AlreadyLeft: already));
    }

    // ── Reading: about a borrower, for whoever is deciding their request ──────

    /// <summary>
    /// What past lenders said about the person asking for this loan.
    /// </summary>
    /// <remarks>
    /// Ben's ask, in his words: <i>"so we know they are trustworthy and respectful with
    /// equipment"</i>. Scoped to the loan being decided rather than offered as a general lookup —
    /// you can read somebody's history because they have asked you for something, not because you
    /// were curious.
    ///
    /// <para>The subject is excluded by the authority check: only somebody who can review <b>this</b>
    /// request may call it, and the borrower of a request is never its reviewer.</para>
    /// </remarks>
    [HttpGet("checkouts/{checkoutId:guid}/borrower-feedback")]
    [Authorize]
    public async Task<ActionResult<BorrowerFeedbackPanelRecord>> GetBorrowerFeedback(
        Guid checkoutId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var checkout = await db.EquipmentCheckouts.AsNoTracking()
            .Include(c => c.EquipmentItem)
            .FirstOrDefaultAsync(c => c.Id == checkoutId, ct);
        if (checkout is null) return NotFound();

        if (!await EquipmentAccess.CanReviewCheckoutAsync(
                _security, checkout.EquipmentItem, userId, IsSuperAdmin(), ct))
            return NotFound();

        // The belt to that braces: even a reviewer must not read their own file, which could happen
        // if somebody ever manages to request their own group's gear while holding the permission.
        if (checkout.BorrowerAppUserId == userId)
            return Ok(EmptyBorrowerPanel);

        var rows = await db.EquipmentLoanFeedbacks.AsNoTracking()
            .Where(f => f.Role == EquipmentFeedbackRole.Lender
                     && f.SubjectAppUserId == checkout.BorrowerAppUserId
                     && f.EquipmentCheckoutId != checkoutId)
            .OrderByDescending(f => f.DateCreated)
            .Select(f => new BorrowerFeedbackRecord(
                f.Id,
                f.CounterpartyComment,
                f.Rating,
                f.AuthorAppUser.DisplayName ?? "A lender",
                f.EquipmentCheckout.EquipmentItem.DisplayName,
                f.EquipmentCheckout.DateReturned ?? f.DateCreated,
                f.DateCreated))
            .Take(20)
            .ToListAsync(ct);

        return Ok(new BorrowerFeedbackPanelRecord(Summarize(rows.Select(r => r.Rating), rows.Count), rows));
    }

    // ── Reading: about a lender, for whoever is considering asking them ───────

    /// <summary>
    /// What past borrowers said about whoever lends this piece.
    /// </summary>
    /// <remarks>
    /// Unattributed, and refused to the lender themselves — the subject-exclusion that matters most
    /// here, since the owner of a piece is exactly the person most likely to open its page.
    /// </remarks>
    [HttpGet("items/{itemId:guid}/lender-feedback")]
    [Authorize]
    public async Task<ActionResult<LenderFeedbackPanelRecord>> GetLenderFeedback(Guid itemId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return NotFound();

        var audience = await EquipmentAccess.ResolveItemAudienceAsync(db, _security, item, userId, IsSuperAdmin(), ct);
        if (audience == EquipmentAccess.ItemAudience.None) return NotFound();

        // The subject is whoever lends this: a person, or the group that owns it. Either way they do
        // not get to read it — this is the check the discrimination test removes.
        if (item.OwnerAppUserId == userId) return NotFound();
        if (item.OwningOrganizationId is Guid orgId
            && await EquipmentAccess.CanManageOrgEquipmentAsync(
                   _security, userId, orgId, false, OrganizationSecurityAction.Read, ct))
            return NotFound();

        var rows = item.OwningOrganizationId is Guid subjectOrgId
            ? await LenderRowsAsync(db, f => f.SubjectOrganizationId == subjectOrgId, ct)
            : item.OwnerAppUserId is Guid subjectUserId
                ? await LenderRowsAsync(db, f => f.SubjectAppUserId == subjectUserId, ct)
                : [];

        return Ok(new LenderFeedbackPanelRecord(Summarize(rows.Select(r => r.Rating), rows.Count), rows));
    }

    private static Task<List<LenderFeedbackRecord>> LenderRowsAsync(
        BenDataContext db, System.Linq.Expressions.Expression<Func<EquipmentLoanFeedback, bool>> subject,
        CancellationToken ct)
        => db.EquipmentLoanFeedbacks.AsNoTracking()
            .Where(f => f.Role == EquipmentFeedbackRole.Borrower)
            .Where(subject)
            .OrderByDescending(f => f.DateCreated)
            .Select(f => new LenderFeedbackRecord(f.Id, f.CounterpartyComment, f.Rating, f.DateCreated))
            .Take(20)
            .ToListAsync(ct);

    // ── Reading: product reviews on a make/model page ─────────────────────────

    /// <summary>Borrowers' remarks about the gear itself, from publicly-listed copies only.</summary>
    /// <remarks>
    /// The same public-only rule the FAQ aggregate follows, for the same reason: a per-viewer
    /// aggregate would say, by its length, that somebody in your group owns one.
    /// </remarks>
    [HttpGet("/api/equipment-catalog/models/{modelId:guid}/reviews")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<ProductReviewRecord>>> GetProductReviews(
        Guid modelId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var reviews = await db.EquipmentLoanFeedbacks.AsNoTracking()
            .Where(f => f.ProductComment != null
                     && f.EquipmentCheckout.EquipmentItem.EquipmentModelId == modelId
                     && f.EquipmentCheckout.EquipmentItem.IncludeInGlobalCatalog)
            .OrderByDescending(f => f.DateCreated)
            .Select(f => new ProductReviewRecord(f.ProductComment!, f.DateCreated))
            .Take(20)
            .ToListAsync(ct);

        return Ok(reviews);
    }

    // ── Moderation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Every piece of feedback touching one group's gear or its members' loans, named on both sides.
    /// </summary>
    /// <remarks>
    /// The only shape that names everybody, because acting on a complaint means knowing who wrote
    /// what about whom. Group Administrators/Owners and SuperAdmin only — not the Equipment
    /// permission, which is about looking after kit rather than adjudicating between people.
    /// </remarks>
    [HttpGet("/api/organizations/{orgId:guid}/equipment-feedback")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<ModeratedFeedbackRecord>>> GetModerationList(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!await IsModeratorAsync(orgId, userId, ct)) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var rows = await db.EquipmentLoanFeedbacks.AsNoTracking()
            .Where(f => f.SubjectOrganizationId == orgId
                     || f.EquipmentCheckout.EquipmentItem.OwningOrganizationId == orgId
                     || f.EquipmentCheckout.BorrowedForOrganizationId == orgId)
            .OrderByDescending(f => f.DateCreated)
            .Select(f => new ModeratedFeedbackRecord(
                f.Id,
                f.EquipmentCheckoutId,
                f.EquipmentCheckout.EquipmentItem.DisplayName,
                f.Role,
                f.AuthorAppUser.DisplayName ?? "Unknown",
                f.SubjectAppUser != null ? f.SubjectAppUser.DisplayName : (f.SubjectOrganization != null ? f.SubjectOrganization.Name : null),
                f.CounterpartyComment,
                f.Rating,
                f.ProductComment,
                f.DateCreated))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>Removes a piece of feedback. Audited by the platform's own audit interceptor.</summary>
    [HttpDelete("/api/organizations/{orgId:guid}/equipment-feedback/{feedbackId:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteFeedback(Guid orgId, Guid feedbackId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (!await IsModeratorAsync(orgId, userId, ct)) return NotFound();

        await using var db = await _db.CreateDbContextAsync(ct);
        var feedback = await db.EquipmentLoanFeedbacks
            .Include(f => f.EquipmentCheckout).ThenInclude(c => c.EquipmentItem)
            .FirstOrDefaultAsync(f => f.Id == feedbackId, ct);
        if (feedback is null) return NotFound();

        // The moderator's remit is their own group's business, not any row they know the id of.
        var inScope = feedback.SubjectOrganizationId == orgId
            || feedback.EquipmentCheckout.EquipmentItem.OwningOrganizationId == orgId
            || feedback.EquipmentCheckout.BorrowedForOrganizationId == orgId;
        if (!inScope && !IsSuperAdmin()) return NotFound();

        db.EquipmentLoanFeedbacks.Remove(feedback);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    private static readonly BorrowerFeedbackPanelRecord EmptyBorrowerPanel =
        new(new LoanFeedbackSummaryRecord(null, 0, 0), []);

    private bool IsSuperAdmin() => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Which side of this loan the caller is, or null if they are neither.</summary>
    private async Task<EquipmentFeedbackRole?> ResolveRoleAsync(
        BenDataContext db, EquipmentCheckout checkout, Guid userId, CancellationToken ct)
    {
        // Borrower first: if somebody is both the borrower and an approver, they borrowed it.
        if (checkout.BorrowerAppUserId == userId) return EquipmentFeedbackRole.Borrower;

        return await EquipmentAccess.CanReviewCheckoutAsync(_security, checkout.EquipmentItem, userId, IsSuperAdmin(), ct)
            ? EquipmentFeedbackRole.Lender
            : null;
    }

    /// <summary>Who a piece of feedback is about, denormalized onto the row when it is written.</summary>
    private static (Guid? UserId, Guid? OrgId) ResolveSubject(EquipmentCheckout checkout, EquipmentFeedbackRole role)
        => role == EquipmentFeedbackRole.Lender
            ? (checkout.BorrowerAppUserId, null)
            // The lender is the item's owner, or the group that owns it — never the borrower's group,
            // which is who the gear went out *for*, not who lent it.
            : (checkout.EquipmentItem.OwnerAppUserId, checkout.EquipmentItem.OwningOrganizationId);

    private static LoanFeedbackSummaryRecord Summarize(IEnumerable<int?> ratings, int commentCount)
    {
        var values = ratings.Where(r => r is not null).Select(r => r!.Value).ToList();
        return new LoanFeedbackSummaryRecord(
            values.Count >= LoanFeedbackSummaryRecord.MinimumRatingsForAverage
                ? Math.Round(values.Average(), 1)
                : null,
            values.Count,
            commentCount);
    }

    private async Task<bool> IsModeratorAsync(Guid orgId, Guid userId, CancellationToken ct)
    {
        if (IsSuperAdmin()) return true;
        if (userId == Guid.Empty) return false;

        await using var db = await _db.CreateDbContextAsync(ct);
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
                        && (m.Role == OrganizationMemberRole.Owner
                            || m.Role == OrganizationMemberRole.Administrator), ct);
    }
}
