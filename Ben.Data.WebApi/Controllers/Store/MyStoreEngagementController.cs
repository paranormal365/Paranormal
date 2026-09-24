using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Store;

/// <summary>
/// A signed-in shopper's favourites and reviews (storefront S6.1): hearts, the product page's "you"
/// panel, writing a review of something bought, and marking another's review helpful.
/// </summary>
/// <remarks>
/// <para><b>Behind the store switch</b> — all of this is about the shop's shelves. Writes count
/// against the cart's limit.</para>
///
/// <para><b>"Bought" means paid and kept</b>: a paid order of the reader's, not cancelled, whose line
/// for this product has not been wholly refunded. Orders placed as a guest with the reader's confirmed
/// email count too — they are joined to the account first (<see cref="StoreGuestOrders"/>).</para>
///
/// <para><b>A review is published by a person, not by writing it.</b> A new review, and every edit
/// of one, waits in the Reviews queue; the stars move only when it is approved.</para>
/// </remarks>
[ApiController]
[Authorize]
[FeatureGated(SiteSettingKeys.FeatureStore)]
[Route("api/me/store/engagement")]
public sealed class MyStoreEngagementController(IDbContextFactory<BenDataContext> dbFactory, TimeProvider? clock = null) : BenControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    // ── Favourites ───────────────────────────────────────────────────────────

    /// <summary>The reader's favourites that are on the store today, most recently added first.</summary>
    [HttpGet("favourites")]
    public async Task<ActionResult<IEnumerable<StoreProductCard>>> Favourites(CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreFavourites.AsNoTracking().Where(f => f.AppUserId == me)
            .OrderByDescending(f => f.DateCreated).Select(f => f.ProductId).ToListAsync(ct);
        var entries = await StoreCatalogue.LoadAsync(db, StoreCatalogue.LiveProducts(db).Where(p => order.Contains(p.Id)), ct);
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var now = Now;
        return Ok(entries.OrderBy(e => order.IndexOf(e.Product.Id)).Select(e => StoreCatalogue.Card(e, settings.LowStockThreshold, now)).ToList());
    }

    [HttpGet("favourites/count")]
    public async Task<ActionResult<StoreFavouriteCount>> FavouriteCount(CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(new StoreFavouriteCount(await CountAsync(db, me, ct)));
    }

    [HttpPut("favourites/{productId:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreFavouriteCount>> AddFavourite(Guid productId, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await StoreCatalogue.LiveProducts(db).AnyAsync(p => p.Id == productId, ct)) return NotFound(StoreReviewSentences.NotOnSale);
        if (!await db.StoreFavourites.AnyAsync(f => f.AppUserId == me && f.ProductId == productId, ct))
        {
            db.StoreFavourites.Add(new StoreFavourite { Id = Guid.NewGuid(), AppUserId = me, ProductId = productId, DateCreated = Now });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* a second tap at the same moment: the unique index kept one */ }
        }
        return Ok(new StoreFavouriteCount(await CountAsync(db, me, ct)));
    }

    [HttpDelete("favourites/{productId:guid}")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<StoreFavouriteCount>> RemoveFavourite(Guid productId, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.StoreFavourites.Where(f => f.AppUserId == me && f.ProductId == productId).ExecuteDeleteAsync(ct);
        return Ok(new StoreFavouriteCount(await CountAsync(db, me, ct)));
    }

    /// <summary>Only favourites on the store today count — a hidden product's heart is kept but not shown.</summary>
    private static Task<int> CountAsync(BenDataContext db, Guid me, CancellationToken ct)
        => db.StoreFavourites.CountAsync(f => f.AppUserId == me && StoreCatalogue.LiveProducts(db).Any(p => p.Id == f.ProductId), ct);

    // ── The product page's "you" panel ───────────────────────────────────────

    [HttpGet("products/{productId:guid}/state")]
    public async Task<ActionResult<StoreProductViewerState>> State(Guid productId, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await StoreGuestOrders.AttachAsync(db, me, ct);

        var favourite = await db.StoreFavourites.AnyAsync(f => f.AppUserId == me && f.ProductId == productId, ct);
        var mine = await db.StoreReviews.AsNoTracking().Where(r => r.ProductId == productId && r.AuthorAppUserId == me)
            .Select(r => new MyStoreReviewRecord(r.Id, r.Rating, r.Title, r.Body, r.Status, r.RejectionReason, r.DateCreated))
            .FirstOrDefaultAsync(ct);
        var bought = await BoughtOrderIdAsync(db, me, productId, ct) is not null;
        return Ok(new StoreProductViewerState(favourite, bought, bought ? null : StoreReviewSentences.OnlyBuyers, mine));
    }

    /// <summary>The newest qualifying order: paid, not cancelled, this product not wholly refunded.</summary>
    internal static Task<Guid?> BoughtOrderIdAsync(BenDataContext db, Guid me, Guid productId, CancellationToken ct)
        => db.StoreOrders.AsNoTracking()
            .Where(o => o.BuyerAppUserId == me && o.PaidUtc != null && o.Status != StoreOrderStatus.Cancelled
                     && o.Items.Any(i => i.ProductId == productId && i.QuantityRefunded < i.Quantity))
            .OrderByDescending(o => o.PaidUtc).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);

    // ── Reviews ──────────────────────────────────────────────────────────────

    /// <summary>Writes or rewrites the reader's review. Either way it waits for approval again.</summary>
    [HttpPut("products/{productId:guid}/review")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<ActionResult<MyStoreReviewRecord>> Review(Guid productId, [FromBody] SubmitStoreReviewRequest request, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        if (StoreReviewSentences.Problem(request) is { } problem) return BadRequest(problem);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await StoreCatalogue.LiveProducts(db).AnyAsync(p => p.Id == productId, ct)) return NotFound(StoreReviewSentences.NotOnSale);
        await StoreGuestOrders.AttachAsync(db, me, ct);
        if (await BoughtOrderIdAsync(db, me, productId, ct) is not { } orderId)
            return StatusCode(StatusCodes.Status403Forbidden, StoreReviewSentences.OnlyBuyers);

        var now = Now;
        var review = await db.StoreReviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.AuthorAppUserId == me, ct);
        var wasApproved = review?.Status == StoreReviewStatus.Approved;
        if (review is null)
        {
            review = new StoreReview
            {
                Id = Guid.NewGuid(), ProductId = productId, AuthorAppUserId = me, OrderId = orderId,
                DateCreated = now, CreatedByAppUserId = me,
            };
            db.StoreReviews.Add(review);
        }
        else
        {
            review.DateUpdated = now;
            review.UpdatedByAppUserId = me;
        }
        review.Rating = request.Rating;
        review.Title = request.Title!.Trim();
        review.Body = request.Body!.Trim();
        review.Status = StoreReviewStatus.Pending;
        review.RejectionReason = null;
        review.ModeratedByAppUserId = null;
        review.ModeratedUtc = null;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict("You've already reviewed this — refresh to see it."); }

        // An approved review taken back into the queue no longer counts toward the stars.
        if (wasApproved) await StoreRatingCaches.RecomputeAsync(db, productId, ct);
        return Ok(new MyStoreReviewRecord(review.Id, review.Rating, review.Title, review.Body, review.Status, null, review.DateCreated));
    }

    [HttpDelete("products/{productId:guid}/review")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public async Task<IActionResult> DeleteReview(Guid productId, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var review = await db.StoreReviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.AuthorAppUserId == me, ct);
        if (review is null) return NotFound();
        await db.StoreReviewVotes.Where(v => v.ReviewId == review.Id).ExecuteDeleteAsync(ct);
        db.StoreReviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await StoreRatingCaches.RecomputeAsync(db, productId, ct);
        return NoContent();
    }

    [HttpPost("reviews/{reviewId:guid}/helpful")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public Task<ActionResult<StoreHelpfulVoteResult>> Helpful(Guid reviewId, CancellationToken ct) => VoteAsync(reviewId, true, ct);

    [HttpDelete("reviews/{reviewId:guid}/helpful")]
    [EnableRateLimiting(RateLimiting.StoreCartPolicy)]
    public Task<ActionResult<StoreHelpfulVoteResult>> NotHelpful(Guid reviewId, CancellationToken ct) => VoteAsync(reviewId, false, ct);

    private async Task<ActionResult<StoreHelpfulVoteResult>> VoteAsync(Guid reviewId, bool helpful, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var review = await db.StoreReviews.AsNoTracking()
            .Where(r => r.Id == reviewId && r.Status == StoreReviewStatus.Approved && StoreCatalogue.LiveProducts(db).Any(p => p.Id == r.ProductId))
            .Select(r => new { r.AuthorAppUserId }).FirstOrDefaultAsync(ct);
        if (review is null) return NotFound();
        if (review.AuthorAppUserId == me) return BadRequest(StoreReviewSentences.NotOwnVote);

        if (helpful)
        {
            if (!await db.StoreReviewVotes.AnyAsync(v => v.ReviewId == reviewId && v.AppUserId == me, ct))
            {
                db.StoreReviewVotes.Add(new StoreReviewVote { Id = Guid.NewGuid(), ReviewId = reviewId, AppUserId = me, DateCreated = Now });
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* twice at once: one vote */ }
            }
        }
        else
        {
            await db.StoreReviewVotes.Where(v => v.ReviewId == reviewId && v.AppUserId == me).ExecuteDeleteAsync(ct);
        }

        // The count is the votes, recounted — never incremented, so a lost race cannot drift it.
        var count = await db.StoreReviewVotes.CountAsync(v => v.ReviewId == reviewId, ct);
        await db.StoreReviews.Where(r => r.Id == reviewId).ExecuteUpdateAsync(s => s.SetProperty(r => r.HelpfulCount, count), ct);
        return Ok(new StoreHelpfulVoteResult(count, helpful));
    }
}
