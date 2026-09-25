using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The store as a shopper sees it: the home page, shelves, search, and one product (storefront S2.2).
/// </summary>
/// <remarks>
/// <para><b>Behind the store switch.</b> With <c>features.store</c> off every address here answers
/// 404 — the shop is hidden, not half there. Pictures are served by
/// <see cref="PublicStoreImageController"/>, which is NOT behind it, so the back office can see
/// them while the shop is dark.</para>
///
/// <para><b>Everything starts from <see cref="StoreCatalogue.LiveProducts"/>.</b> A hidden
/// product answers exactly as a missing one does, to everybody except a SuperAdmin asking for a
/// preview — so a shopper cannot tell "not yet" from "never".</para>
/// </remarks>
[ApiController]
[Route("api/public/store")]
[AllowAnonymous]
[FeatureGated(SiteSettingKeys.FeatureStore)]
[EnableRateLimiting(RateLimiting.StoreBrowsePolicy)]
public sealed class PublicStoreController(
    IDbContextFactory<BenDataContext> dbFactory, StorePaymentSetup payments, IOptions<StripeOptions> stripe)
    : BenControllerBase
{
    public const int RailSize = 8;
    public const int SuggestionCount = 8;
    public const string NoSuchCategory = "There's no such category.";
    public const string NoSuchProduct = "That product isn't in the store.";

    /// <summary>What every store page needs to know about the shop.</summary>
    [HttpGet("info")]
    public async Task<ActionResult<StorePublicInfo>> Info(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await StoreSettingsReader.ReadAsync(db, ct);
        var support = s.SupportEmail ?? await SiteSettingsService.GetAsync(db, SiteSettingKeys.PublicContactEmail, ct);
        var paymentsEnabled = payments.Fake || (payments.HasSecretKey && payments.HasPublishableKey);
        return Ok(new StorePublicInfo(
            s.CheckoutEnabled, s.ShippingFlatRate, s.FreeShippingThreshold > 0 ? s.FreeShippingThreshold : null,
            s.ReturnsWindowDays, support, paymentsEnabled, payments.Fake,
            payments.HasPublishableKey ? stripe.Value.PublishableKey : null));
    }

    [HttpGet("home")]
    public async Task<ActionResult<StoreHomeResponse>> Home(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await StoreSettingsReader.ReadAsync(db, ct);
        var now = DateTime.UtcNow;
        var entries = await StoreCatalogue.LoadAsync(db, StoreCatalogue.LiveProducts(db), ct);
        var categories = await CategoryCardsAsync(db, entries, ct);

        StoreProductCard Card(StoreCatalogue.Entry e) => StoreCatalogue.Card(e, s.LowStockThreshold, now);

        Response.Headers.CacheControl = "public, max-age=60";
        var topLevel = categories.Where(c => c.ParentId is null).ToList();
        return Ok(new StoreHomeResponse(
            Slides: topLevel.Where(c => c.ImageUploadFileId is not null).Take(RailSize)
                .Select(c => new StoreHeroSlide(c.Name, c.Description, c.ImageUploadFileId!.Value, $"/store/c/{c.Slug}")).ToList(),
            Categories: topLevel,
            Featured: StoreCatalogue.Popular(entries.Where(e => e.Product.IsFeatured)).Take(RailSize).Select(Card).ToList(),
            NewArrivals: entries.OrderByDescending(e => StoreCatalogue.IsNew(e.Product, now)).ThenByDescending(e => e.Product.DateCreated)
                .Take(RailSize).Select(Card).ToList(),
            Popular: StoreCatalogue.Popular(entries).Take(RailSize).Select(Card).ToList()));
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IEnumerable<StoreCategoryCard>>> Categories(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entries = await StoreCatalogue.LoadAsync(db, StoreCatalogue.LiveProducts(db), ct);
        return Ok(await CategoryCardsAsync(db, entries, ct));
    }

    /// <summary>One shelf, filtered, sorted and paged.</summary>
    [HttpGet("categories/{slug}")]
    public async Task<ActionResult<StoreListingResponse>> Category(string slug, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var category = await db.StoreCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive && (c.ParentCategoryId == null || c.ParentCategory!.IsActive), ct);
        if (category is null) return NotFound(NoSuchCategory);
        // A shelf with nothing on sale under it is for sellers, not shoppers: its address answers
        // exactly as a missing one does (Ben, 09/24).
        if (!await StoreCatalogue.LiveProducts(db).AnyAsync(p => p.CategoryId == category.Id || p.Category.ParentCategoryId == category.Id, ct))
            return NotFound(NoSuchCategory);
        return Ok(await ListingAsync(db, category.Id, ct));
    }

    /// <summary>Every product, or a search (<c>?q=</c>), filtered, sorted and paged.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<StoreListingResponse>> Products(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListingAsync(db, null, ct));
    }

    /// <summary>
    /// One product. <c>?preview=1</c> from a SuperAdmin also answers for a hidden product or one on
    /// a hidden shelf; from anybody else it answers exactly as for a missing one.
    /// </summary>
    [HttpGet("products/{slug}")]
    public async Task<ActionResult<StoreProductDetail>> Product(string slug, [FromQuery] int? preview, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var previewing = preview == 1 && await CallerIsSuperAdminAsync();
        Response.Headers.CacheControl = "no-store";

        var product = await (previewing ? db.StoreProducts : StoreCatalogue.LiveProducts(db)).AsNoTracking()
            .Where(p => p.Slug == slug).Select(p => new { p.Id, Live = p.IsActive && p.Category.IsActive && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive) })
            .FirstOrDefaultAsync(ct);
        // Store sellers P13: a version its newer version replaced keeps its page — "no longer made,
        // replaced by" — so a link or a bookmark doesn't end on nothing. Never buyable.
        var discontinued = false;
        if (product is null)
        {
            product = await db.StoreProducts.AsNoTracking()
                .Where(p => p.Slug == slug && !p.IsActive && p.DiscontinuedUtc != null
                         && p.Category.IsActive && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive))
                .Select(p => new { p.Id, Live = false }).FirstOrDefaultAsync(ct);
            discontinued = product is not null;
        }
        if (product is null) return NotFound(NoSuchProduct);

        var entries = await StoreCatalogue.LoadAsync(db, db.StoreProducts.Where(p => p.Id == product.Id), ct);
        var e = entries.Single();
        var s = await StoreSettingsReader.ReadAsync(db, ct);
        var now = DateTime.UtcNow;

        // Only the choices a buyer can actually make: an offered value that at least one live
        // variant is made of. A value whose variants are all sold out stays — "Sold out" is worth
        // seeing — but a retired one is gone.
        var usable = e.Values.Select(v => (v.Option, v.Value)).ToHashSet();
        var options = await db.StoreProductOptions.AsNoTracking().Where(o => o.ProductId == e.Product.Id)
            .OrderBy(o => o.SortOrder)
            .Select(o => new { o.Id, o.Name, o.Kind, Values = o.Values.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ToList() })
            .ToListAsync(ct);
        var optionRecords = options
            .Select(o => new StoreOptionRecord(o.Id, o.Name, o.Kind,
                o.Values.Where(v => usable.Contains((o.Name, v.Value)))
                    .Select(v => new StoreOptionValueRecord(v.Id, v.Value, v.SwatchHex, v.SortOrder, true)).ToList()))
            .Where(o => o.Values.Count > 0)
            .ToList();

        var chosenValues = await db.StoreProductVariantOptionValues.AsNoTracking()
            .Where(x => x.Variant.ProductId == e.Product.Id && x.Variant.IsActive)
            .Select(x => new { x.VariantId, x.OptionValueId }).ToListAsync(ct);
        var variants = e.Variants.Select(v => new StoreVariantPublicRecord(
            v.Id, v.Sku, StorePriceCaches.Label(v.Name), v.Price, v.CompareAtPrice is { } was && was > v.Price ? was : null,
            StoreCatalogue.Available(v), chosenValues.Where(x => x.VariantId == v.Id).Select(x => x.OptionValueId).ToList(),
            v.IsDefault)).ToList();

        var images = await db.StoreProductImages.AsNoTracking().Where(i => i.ProductId == e.Product.Id).OrderBy(i => i.SortOrder)
            .Select(i => new StoreImageRecord(i.Id, i.UploadFileId, i.AltText, i.SortOrder, i.VariantId,
                db.UploadFileMetadata.Where(m => m.UploadFileId == i.UploadFileId).Select(m => m.WidthPixels ?? 0).FirstOrDefault(),
                db.UploadFileMetadata.Where(m => m.UploadFileId == i.UploadFileId).Select(m => m.HeightPixels ?? 0).FirstOrDefault()))
            .ToListAsync(ct);
        var specs = await db.StoreProductSpecs.AsNoTracking().Where(x => x.ProductId == e.Product.Id).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var ratings = await db.StoreReviews.AsNoTracking()
            .Where(r => r.ProductId == e.Product.Id && r.Status == StoreReviewStatus.Approved && r.Product.ReviewsEnabled).Select(r => r.Rating).ToListAsync(ct);

        StoreEquipmentLink? equipment = null;
        if (e.Product.EquipmentModelId is { } modelId)
            equipment = await db.EquipmentModels.AsNoTracking().Where(m => m.Id == modelId && m.UrlName != null && m.EquipmentBrand.UrlName != null)
                .Select(m => new StoreEquipmentLink(m.EquipmentBrand.Name, m.EquipmentBrand.UrlName!, m.Name, m.UrlName!))
                .FirstOrDefaultAsync(ct);

        // Store sellers P12: the FAQ, when it's switched on. Names nobody and dates nothing.
        var faqs = e.Product.FaqEnabled
            ? await db.StoreProductFaqs.AsNoTracking().Where(f => f.ProductId == e.Product.Id).OrderBy(f => f.SortOrder).ThenBy(f => f.DateCreated)
                .Select(f => new StoreFaqView(f.Id, f.Question, f.Answer)).ToListAsync(ct)
            : [];

        // Store sellers P13: the versions either side, when a shopper can reach them.
        var newer = await StoreCatalogue.LiveProducts(db).AsNoTracking().Where(n => n.PreviousVersionProductId == e.Product.Id)
            .Select(n => new StoreVersionLink(n.Id, n.Name, n.Slug, n.VersionLabel)).FirstOrDefaultAsync(ct);
        var older = e.Product.PreviousVersionProductId is { } olderId
            ? await db.StoreProducts.AsNoTracking()
                .Where(o => o.Id == olderId && (o.IsActive || o.DiscontinuedUtc != null)
                         && o.Category.IsActive && (o.Category.ParentCategoryId == null || o.Category.ParentCategory!.IsActive))
                .Select(o => new StoreVersionLink(o.Id, o.Name, o.Slug, o.VersionLabel)).FirstOrDefaultAsync(ct)
            : null;

        var related = StoreCatalogue.Popular(
                (await StoreCatalogue.LoadAsync(db, StoreCatalogue.LiveProducts(db)
                    .Where(p => p.CategoryId == e.Product.CategoryId && p.Id != e.Product.Id), ct)))
            .Take(RailSize).Select(x => StoreCatalogue.Card(x, s.LowStockThreshold, now)).ToList();

        return Ok(new StoreProductDetail(
            e.Product.Id, e.Product.Name, e.Product.Slug, e.Category.Name, e.Category.Slug, e.Product.ShortDescription,
            e.Product.LongDescriptionHtml, StoreCatalogue.IsNew(e.Product, now), e.Product.IsFeatured, images, optionRecords,
            variants,
            specs.GroupBy(x => x.GroupName).Select(g => new StoreSpecGroup(g.Key, g.Select(x => new StoreSpecRecord(x.Name, x.Value)).ToList())).ToList(),
            new StoreReviewSummary(ratings.Count == 0 ? 0m : Math.Round((decimal)ratings.Sum() / ratings.Count, 2, MidpointRounding.AwayFromZero),
                ratings.Count, Enumerable.Range(1, 5).Select(n => ratings.Count(r => r == n)).ToList()),
            equipment, related, s.LowStockThreshold, s.ReturnsWindowDays,
            e.Product.DateUpdated ?? e.Product.DateCreated, IsPreview: !product.Live && !discontinued,
            e.Category.ParentCategory?.Name, e.Category.ParentCategory?.Slug,
            Faqs: faqs, CanAsk: product.Live,
            VersionLabel: e.Product.VersionLabel, NewerVersion: newer, OlderVersion: older, Discontinued: discontinued,
            ReviewsEnabled: e.Product.ReviewsEnabled, ReturnPolicyText: e.Product.ReturnPolicyText, WarrantyText: e.Product.WarrantyText,
            Videos: await StoreProductRecords.VideosAsync(db, e.Product.Id, ct)));
    }

    /// <summary>Counts a look at a product, for "most popular". Always 204 — a hidden or missing product is not news to the caller.</summary>
    public const int ReviewPageSize = 10;

    /// <summary>
    /// A product's approved reviews (storefront S6.1), a page at a time. Most popular first —
    /// the most helpful, then the newest.
    /// </summary>
    [HttpGet("products/{slug}/reviews")]
    public async Task<ActionResult<StoreReviewPage>> Reviews(string slug, [FromQuery] string? sort, [FromQuery] int? page, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var productId = await StoreCatalogue.LiveProducts(db).Where(p => p.Slug == slug).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (productId is null) return NotFound(NoSuchProduct);

        var me = await GetCurrentUserIdOrNullAcrossSchemesAsync();
        var key = StoreReviewSorts.Normalize(sort);
        // Store sellers P14: an item with reviews switched off shows none.
        var approved = db.StoreReviews.AsNoTracking().Where(r => r.ProductId == productId && r.Status == StoreReviewStatus.Approved && r.Product.ReviewsEnabled);
        var sorted = key switch
        {
            StoreReviewSorts.Newest => approved.OrderByDescending(r => r.DateCreated),
            StoreReviewSorts.Highest => approved.OrderByDescending(r => r.Rating).ThenByDescending(r => r.DateCreated),
            StoreReviewSorts.Lowest => approved.OrderBy(r => r.Rating).ThenByDescending(r => r.DateCreated),
            StoreReviewSorts.Helpful => approved.OrderByDescending(r => r.HelpfulCount).ThenByDescending(r => r.Rating).ThenByDescending(r => r.DateCreated),
            _ => approved.OrderByDescending(r => r.HelpfulCount).ThenByDescending(r => r.DateCreated),
        };
        var total = await approved.CountAsync(ct);
        var number = Math.Max(1, page ?? 1);
        var rows = await sorted.Skip((number - 1) * ReviewPageSize).Take(ReviewPageSize)
            .Select(r => new
            {
                r.Id, r.Rating, r.Title, r.Body, r.AuthorAppUserId, Author = r.AuthorAppUser.DisplayName ?? r.AuthorAppUser.FirstName,
                r.DateCreated, r.HelpfulCount, r.AdminReply, r.AdminRepliedUtc,
                Voted = me != null && db.StoreReviewVotes.Any(v => v.ReviewId == r.Id && v.AppUserId == me),
            })
            .ToListAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return Ok(new StoreReviewPage(rows.Select(r => new StoreReviewRecord(
                r.Id, r.Rating, r.Title, r.Body, string.IsNullOrWhiteSpace(r.Author) ? "A buyer" : r.Author!, r.DateCreated, r.HelpfulCount,
                r.AdminReply, r.AdminRepliedUtc, r.AuthorAppUserId == me, r.Voted)).ToList(),
            total, number, ReviewPageSize, key));
    }

    [HttpPost("products/{id:guid}/viewed")]
    public async Task<IActionResult> Viewed(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await StoreCatalogue.LiveProducts(db).Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1), ct);
        return NoContent();
    }

    /// <summary>What the search box offers as somebody types: two characters or more, eight at most.</summary>
    [HttpGet("search-suggest")]
    public async Task<ActionResult<IEnumerable<StoreSearchSuggestion>>> Suggest([FromQuery] string? q, CancellationToken ct)
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < 2) return Ok(Array.Empty<StoreSearchSuggestion>());

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entries = await StoreCatalogue.LoadAsync(db, StoreCatalogue.LiveProducts(db), ct);
        return Ok(entries
            .Where(e => e.Product.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || e.Variants.Any(v => v.Sku.StartsWith(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Product.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(e => e.Product.Name)
            .Take(SuggestionCount)
            .Select(e => new StoreSearchSuggestion(e.Product.Name, e.Product.Slug, e.Picture?.UploadFileId, StoreCatalogue.MinPrice(e)))
            .ToList());
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private async Task<StoreListingResponse> ListingAsync(BenDataContext db, Guid? categoryId, CancellationToken ct)
    {
        var query = StoreListingQueryString.Parse(Request.QueryString.Value);
        var s = await StoreSettingsReader.ReadAsync(db, ct);
        var now = DateTime.UtcNow;

        var entries = await StoreCatalogue.LoadAsync(db, StoreCatalogue.LiveProducts(db), ct);
        var categories = await CategoryCardsAsync(db, entries, ct);

        var shelf = entries.Where(e => categoryId is null || e.Product.CategoryId == categoryId || e.Category.ParentCategoryId == categoryId)
                           .Where(e => StoreCatalogue.MatchesSearch(e, query.Q)).ToList();
        var matching = StoreCatalogue.Sort(shelf.Where(e => StoreCatalogue.Matches(e, query)), query.Sort!).ToList();

        var pages = Math.Max(1, (int)Math.Ceiling(matching.Count / (double)StoreCatalogConstants.StorePageSize));
        var page = Math.Min(query.Page, pages);
        var cards = matching.Skip((page - 1) * StoreCatalogConstants.StorePageSize).Take(StoreCatalogConstants.StorePageSize)
            .Select(e => StoreCatalogue.Card(e, s.LowStockThreshold, now)).ToList();

        return new StoreListingResponse(
            categoryId is { } id ? categories.FirstOrDefault(c => c.Id == id) : null, categories, cards,
            matching.Count, page, StoreCatalogConstants.StorePageSize, StoreCatalogue.Facets(shelf));
    }

    /// <summary>Shown shelves with a live product on them, in the admin's order.</summary>
    /// <summary>
    /// The shelves a shopper sees, as a tree read top to bottom: each top-level category followed by
    /// its subcategories. Only those with something on sale under them (Ben, 09/24) — a parent counts
    /// its subcategories' products, so it shows when only a subcategory has stock.
    /// </summary>
    internal static async Task<List<StoreCategoryCard>> CategoryCardsAsync(
        BenDataContext db, IReadOnlyList<StoreCatalogue.Entry> entries, CancellationToken ct)
    {
        // Every entry is live, so its category and its parent are both shown.
        var own = entries.GroupBy(e => e.Product.CategoryId).ToDictionary(g => g.Key, g => g.Count());
        var underParent = entries.Where(e => e.Category.ParentCategoryId is not null)
            .GroupBy(e => e.Category.ParentCategoryId!.Value).ToDictionary(g => g.Key, g => g.Count());
        int Count(Guid id) => own.GetValueOrDefault(id) + underParent.GetValueOrDefault(id);

        var categories = await db.StoreCategories.AsNoTracking().Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        var children = categories.Where(c => c.ParentCategoryId is not null).ToLookup(c => c.ParentCategoryId!.Value);

        StoreCategoryCard Card(StoreCategory c) =>
            new(c.Id, c.Name, c.Slug, c.Description, c.ImageUploadFileId, c.IsNew, Count(c.Id), c.ParentCategoryId);

        var cards = new List<StoreCategoryCard>();
        foreach (var top in categories.Where(c => c.ParentCategoryId is null && Count(c.Id) > 0))
        {
            cards.Add(Card(top));
            cards.AddRange(children[top.Id].Where(c => Count(c.Id) > 0).Select(Card));
        }
        return cards;
    }
}
