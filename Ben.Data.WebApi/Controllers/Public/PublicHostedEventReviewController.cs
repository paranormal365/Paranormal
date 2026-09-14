using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// What people who came to an event thought of it (item 235 phase 12).
/// </summary>
/// <remarks>
/// The same shapes as a tour's reviews, so a page reads either the same way. Anonymous to read: a
/// review nobody can read before signing in is written for nobody. Hidden ones are absent, except to
/// the person who wrote one, who is told it was hidden rather than left wondering where it went.
/// </remarks>
[ApiController]
[Route("api/public/hosted-events/{eventId:guid}")]
public sealed class PublicHostedEventReviewController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicHostedEventReviewController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet("reviews")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicHostedEventReviewsRecord>> Get(Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId
                                   && e.LifecycleState != Ben.Data.Common.Enums.HostedEventLifecycleState.Draft, ct);
        if (ev is null) return NotFound();

        return Ok(await DescribeAsync(db, ev, GetCurrentUserIdOrNull(), ct));
    }

    /// <summary>Leaves or changes this person's review. Rewriting it clears a hiding.</summary>
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    [HttpPut("my-review")]
    [Authorize]
    public async Task<ActionResult<PublicHostedEventReviewsRecord>> Upsert(
        Guid eventId, [FromBody] UpsertTourReviewRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (request.Stars is < 1 or > 5) return BadRequest("A rating is one to five stars.");
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > 1000 }) return BadRequest("That is longer than 1,000 characters.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        var now = DateTime.UtcNow;
        if (await HostedEventReviews.WhyNotAsync(db, ev, userId, now, ct) is { } refusal) return BadRequest(refusal);

        var existing = await db.HostedEventReviews
            .FirstOrDefaultAsync(r => r.HostedEventId == eventId && r.AppUserId == userId, ct);

        if (existing is null)
        {
            db.HostedEventReviews.Add(new HostedEventReview
            {
                Id = Guid.NewGuid(), HostedEventId = eventId, AppUserId = userId, Stars = request.Stars,
                Comment = comment, DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        else
        {
            existing.Stars = request.Stars;
            existing.Comment = comment;
            // Rewritten words are new words, judged afresh.
            existing.HiddenAtUtc = null;
            existing.HiddenByAppUserId = null;
            existing.DateUpdated = now;
            existing.UpdatedByAppUserId = userId;
        }

        await db.SaveChangesAsync(ct);
        return Ok(await DescribeAsync(db, ev, userId, ct));
    }

    [HttpDelete("my-review")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var mine = await db.HostedEventReviews
            .FirstOrDefaultAsync(r => r.HostedEventId == eventId && r.AppUserId == userId, ct);
        if (mine is null) return NotFound();

        db.HostedEventReviews.Remove(mine);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    internal static async Task<PublicHostedEventReviewsRecord> DescribeAsync(
        BenDataContext db, HostedEvent ev, Guid? userId, CancellationToken ct)
    {
        var rows = await db.HostedEventReviews.AsNoTracking()
            .Where(r => r.HostedEventId == ev.Id && (r.HiddenAtUtc == null || r.AppUserId == userId))
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new TourReviewRecord(
                r.Id,
                r.AppUser.DisplayName ?? r.AppUser.UserName ?? "A guest",
                r.AppUser.Handle,
                r.Stars, r.Comment, r.DateCreated,
                r.AppUserId == userId,
                r.HiddenAtUtc != null))
            .ToListAsync(ct);

        var (average, count) = await HostedEventReviews.RatingAsync(db, ev.Id, ct);
        var (pastAverage, pastCount) = await HostedEventReviews.PastRatingAsync(db, ev.OrganizationId, ev.Id, ct);
        var whyNot = await HostedEventReviews.WhyNotAsync(db, ev, userId, DateTime.UtcNow, ct);

        return new PublicHostedEventReviewsRecord(
            new TourReviewsRecord(rows, average, count, whyNot is null, whyNot, rows.FirstOrDefault(r => r.IsMine)),
            ev.Name, ev.UrlName, ev.Organization?.Name, ev.Organization?.UrlName, ev.EndsOn,
            ev.LifecycleState is Ben.Data.Common.Enums.HostedEventLifecycleState.Ended
                              or Ben.Data.Common.Enums.HostedEventLifecycleState.Archived,
            pastAverage, pastCount);
    }
}
