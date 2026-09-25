using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// A product's star average and review count, kept on the product so the listing can sort by
/// rating without reading every review (storefront S1.6).
/// </summary>
/// <remarks>
/// Counts APPROVED reviews only — a review waiting for moderation, or one refused, has not been
/// published and must not move the stars. Recomputed on every moderation verb and on delete, the
/// only paths that change what is approved — and on the store's reviews switch (store sellers P14):
/// an item with reviews off has no stars anywhere.
/// </remarks>
public static class StoreRatingCaches
{
    public static async Task RecomputeAsync(BenDataContext db, Guid productId, CancellationToken ct = default)
    {
        var ratings = await db.StoreReviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == StoreReviewStatus.Approved && r.Product.ReviewsEnabled)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        var average = ratings.Count == 0 ? 0m : Math.Round((decimal)ratings.Sum() / ratings.Count, 2, MidpointRounding.AwayFromZero);
        await db.StoreProducts.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AverageRating, average).SetProperty(p => p.ReviewCount, ratings.Count), ct);
    }
}
