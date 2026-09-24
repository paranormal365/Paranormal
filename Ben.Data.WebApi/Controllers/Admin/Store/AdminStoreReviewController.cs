using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The review queue: buyers' reviews wait here until a SuperAdmin approves or refuses them
/// (storefront S1.6).
/// </summary>
/// <remarks>
/// <para><b>A refusal carries its reason</b>, because the reviewer is told why — "rejected" with
/// nothing after it reads as censorship, and the reviewer cannot fix what they cannot see.</para>
///
/// <para><b>A reply goes under an approved review only.</b> Replying to one nobody can see would
/// publish the shop's half of a conversation whose other half is hidden.</para>
///
/// <para>Every verb that changes what is approved recomputes the product's stars
/// (<see cref="StoreRatingCaches"/>).</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/reviews")]
public sealed class AdminStoreReviewController(IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog)
    : BenControllerBase
{
    public const int MaxReasonLength = 500;
    public const int MaxReplyLength = 1000;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreReviewAdminRecord>>> GetAll(
        [FromQuery] StoreReviewStatus? status, [FromQuery] string? q, [FromQuery] Guid? productId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.StoreReviews.AsNoTracking();
        if (status is { } s) query = query.Where(r => r.Status == s);
        if (productId is { } p) query = query.Where(r => r.ProductId == p);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(r => r.Title.ToLower().Contains(term) || r.Body.ToLower().Contains(term)
                                  || r.Product.Name.ToLower().Contains(term));
        }

        // Oldest waiting first — the queue is worked from the front.
        var rows = await Project(db, status == StoreReviewStatus.Pending
                ? query.OrderBy(r => r.DateCreated)
                : query.OrderByDescending(r => r.DateCreated))
            .ToListAsync(ct);
        return Ok(ListPaging.Apply(rows, page, pageSize, Response));
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<StoreReviewAdminRecord>> Approve(Guid id, CancellationToken ct)
        => await ModerateAsync(id, StoreReviewStatus.Approved, reason: null, ct);

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<StoreReviewAdminRecord>> Reject(Guid id, [FromBody] RejectStoreReviewRequest request, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return BadRequest("Give a reason — the reviewer is told why.");
        if (reason.Length > MaxReasonLength) return BadRequest($"A reason is {MaxReasonLength} characters at most.");
        return await ModerateAsync(id, StoreReviewStatus.Rejected, reason, ct);
    }

    /// <summary>The shop's answer under an approved review. Empty clears it.</summary>
    [HttpPut("{id:guid}/reply")]
    public async Task<ActionResult<StoreReviewAdminRecord>> Reply(Guid id, [FromBody] ReplyToStoreReviewRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var review = await db.StoreReviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null) return NotFound();

        var body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim();
        if (body is not null && review.Status != StoreReviewStatus.Approved)
            return BadRequest("Approve the review before replying to it.");
        if (body?.Length > MaxReplyLength) return BadRequest("A reply is 1,000 characters at most.");

        review.AdminReply = body;
        review.AdminReplyByAppUserId = body is null ? null : userId;
        review.AdminRepliedUtc = body is null ? null : DateTime.UtcNow;
        review.DateUpdated = DateTime.UtcNow;
        review.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(await Project(db, db.StoreReviews.Where(r => r.Id == id)).SingleAsync(ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var review = await db.StoreReviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null) return NotFound();

        db.StoreReviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await StoreRatingCaches.RecomputeAsync(db, review.ProductId, ct);
        await TryAuditAsync(auditLog.LogDeleteAsync(nameof(StoreReview), id, review, userId, AppSources.WebApi));
        return NoContent();
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private async Task<ActionResult<StoreReviewAdminRecord>> ModerateAsync(
        Guid id, StoreReviewStatus to, string? reason, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var review = await db.StoreReviews.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null) return NotFound();

        var now = DateTime.UtcNow;
        review.Status = to;
        review.RejectionReason = reason;
        review.ModeratedByAppUserId = userId;
        review.ModeratedUtc = now;
        // A refused review keeps no reply: the shop's answer would stand under nothing.
        if (to == StoreReviewStatus.Rejected)
        {
            review.AdminReply = null;
            review.AdminReplyByAppUserId = null;
            review.AdminRepliedUtc = null;
        }
        review.DateUpdated = now;
        review.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        await StoreRatingCaches.RecomputeAsync(db, review.ProductId, ct);
        return Ok(await Project(db, db.StoreReviews.Where(r => r.Id == id)).SingleAsync(ct));
    }

    private static IQueryable<StoreReviewAdminRecord> Project(BenDataContext db, IQueryable<StoreReview> reviews)
        => reviews.Select(r => new StoreReviewAdminRecord(
            r.Id, r.ProductId, r.Product.Name, r.Product.Slug, r.AuthorAppUserId,
            db.AppUsers.Where(u => u.Id == r.AuthorAppUserId).Select(u => u.DisplayName ?? u.UserName ?? "").FirstOrDefault() ?? "",
            db.StoreOrders.Where(o => o.Id == r.OrderId).Select(o => o.OrderNumber).FirstOrDefault(),
            r.Rating, r.Title, r.Body, r.Status, r.RejectionReason, r.HelpfulCount, r.DateCreated, r.ModeratedUtc,
            r.AdminReply, r.AdminRepliedUtc));
}
