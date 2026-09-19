using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Publications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Publications;

/// <summary>
/// Subscribing to a publication, and the caller's own list of them.
/// </summary>
/// <remarks>
/// <para>Reading a publication needs no account; subscribing does, because a subscription is
/// somebody the group can reach and — eventually — bill. That is the line the public controller
/// draws on the other side of.</para>
///
/// <para><b>Unsubscribing marks rather than deletes.</b> Unlike a feed follow, which is deleted
/// outright because a soft-deleted follow is a record of who once read whom, a subscription is what
/// a payment would attach to and a cancelled one has to stay answerable for what it covered.
/// Re-subscribing clears the cancellation on the same row, so one person has at most one
/// subscription per publication — which is what the unique index says.</para>
/// </remarks>
[ApiController]
[Route("api/me/publication-subscriptions")]
[Authorize]
public sealed class MySubscriptionController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MySubscriptionController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>What the caller subscribes to, most recently active first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MySubscriptionRecord>>> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        var subscriptions = await db.PublicationSubscriptions.AsNoTracking()
            .Where(s => s.SubscriberAppUserId == userId && s.CancelledUtc == null)
            .Select(s => new MySubscriptionRecord(
                s.Publication.UrlName,
                s.Publication.Title,
                s.Publication.Organization.Name,
                s.DateCreated,
                s.Publication.Posts.Where(p => p.PublishedUtc != null).Max(p => p.PublishedUtc)))
            .ToListAsync(ct);

        return Ok(subscriptions.OrderByDescending(s => s.LatestPostUtc ?? s.SubscribedUtc).ToList());
    }

    /// <summary>Whether the caller subscribes to one publication.</summary>
    [HttpGet("{urlName}")]
    public async Task<ActionResult<bool>> IsSubscribed(string urlName, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        return Ok(await db.PublicationSubscriptions.AsNoTracking()
            .AnyAsync(s => s.SubscriberAppUserId == userId
                        && s.CancelledUtc == null
                        && s.Publication.UrlName == urlName, ct));
    }

    /// <summary>Subscribes. Idempotent, and revives a cancelled subscription rather than adding one.</summary>
    [HttpPost("{urlName}")]
    public async Task<IActionResult> Subscribe(string urlName, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        // Only a public publication can be subscribed to. Otherwise a guessed URL name would let
        // somebody attach themselves to something the group has not shown anyone.
        var publication = await db.Publications
            .FirstOrDefaultAsync(p => p.UrlName == urlName && p.IsPublic, ct);
        if (publication is null) return NotFound();

        var existing = await db.PublicationSubscriptions
            .FirstOrDefaultAsync(s => s.PublicationId == publication.Id
                                   && s.SubscriberAppUserId == userId, ct);

        if (existing is not null)
        {
            // Already subscribed, or subscribed again after cancelling. Either way one row.
            existing.CancelledUtc = null;
        }
        else
        {
            db.PublicationSubscriptions.Add(new PublicationSubscription
            {
                Id = Guid.NewGuid(),
                PublicationId = publication.Id,
                SubscriberAppUserId = userId,
                Tier = null,          // free; see the entity for why the column exists
                DateCreated = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Unsubscribes. Idempotent.</summary>
    [HttpDelete("{urlName}")]
    public async Task<IActionResult> Unsubscribe(string urlName, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await OrgPublicationController.PublicationsEnabledAsync(db, ct)) return NotFound();

        var subscription = await db.PublicationSubscriptions
            .FirstOrDefaultAsync(s => s.SubscriberAppUserId == userId
                                   && s.CancelledUtc == null
                                   && s.Publication.UrlName == urlName, ct);

        if (subscription is not null)
        {
            subscription.CancelledUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return NoContent();
    }
}
