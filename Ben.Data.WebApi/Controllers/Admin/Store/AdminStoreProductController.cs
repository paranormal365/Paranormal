using System.Text.RegularExpressions;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The store's products: details, options and the variants they make, stock, and pictures
/// (storefront S1.4).
/// </summary>
/// <remarks>
/// <para><b>A product starts hidden.</b> Creating one needs only a name; it arrives with a single
/// $0.00 variant to price, and going on sale is its own verb with its own checks — a price, a
/// picture, a live variant, a visible category — each refused with the thing to do next.</para>
///
/// <para><b>What has been sold stays.</b> A product or variant an order names can be switched off
/// but not deleted: the order, the invoice and the refund all point at it.</para>
///
/// <para><b>Every stock change leaves a movement.</b> The count is only ever changed through
/// <see cref="StoreStock"/>, whose conditional update refuses to leave fewer on the shelf than
/// open checkouts are holding; saving a variant never touches its stock.</para>
///
/// <para>Every change to variants or options recomputes the product's price range and variant
/// names (<see cref="StorePriceCaches"/>) before it answers.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/products")]
public sealed partial class AdminStoreProductController(
    IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog, StoreImageStorage images,
    ICmsMarkupSanitizer sanitizer) : BenControllerBase
{
    public const int MaxNameLength = StoreProductEditor.MaxNameLength;
    public const int MaxShortDescriptionLength = StoreProductEditor.MaxShortDescriptionLength;
    public const int MaxLongDescriptionLength = StoreProductEditor.MaxLongDescriptionLength;

    private readonly StoreProductEditor _editor = new(sanitizer, images);
    private StoreEditActor Me => new(GetCurrentUserIdOrThrow(), StoreChangeActor.Store);

    // ── products ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreProductListAdminRecord>>> GetAll(
        [FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] bool? active,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await StoreSettingsReader.ReadAsync(db, ct);

        var query = db.StoreProducts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term) || p.Slug.Contains(term)
                                  || p.Variants.Any(v => v.Sku.ToLower().Contains(term)));
        }
        if (categoryId is { } category) query = query.Where(p => p.CategoryId == category);
        if (active is { } on) query = query.Where(p => p.IsActive == on);

        var threshold = settings.LowStockThreshold;
        var rows = await query
            .OrderBy(p => p.Category.SortOrder).ThenBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new StoreProductListAdminRecord(
                p.Id, p.Name, p.Slug, p.CategoryId,
                p.Category.ParentCategory == null ? p.Category.Name : p.Category.ParentCategory.Name + " › " + p.Category.Name,
                p.IsActive, p.IsActive && !(p.Category.IsActive && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive)),
                p.IsFeatured, p.MinPrice, p.MaxPrice,
                p.Variants.Where(v => v.IsActive).Sum(v => v.StockOnHand),
                p.Variants.Count(v => v.IsActive && v.StockOnHand - v.StockReserved <= threshold),
                p.Variants.Count(), p.UnitsSold,
                p.Images.Where(i => i.VariantId == null).OrderBy(i => i.SortOrder).Select(i => (Guid?)i.UploadFileId).FirstOrDefault()
                    ?? p.Images.OrderBy(i => i.SortOrder).Select(i => (Guid?)i.UploadFileId).FirstOrDefault(),
                p.DateUpdated ?? p.DateCreated,
                p.SellerAppUser == null ? null : p.SellerAppUser.DisplayName ?? p.SellerAppUser.Email,
                db.StoreProductSaleRequests.Any(r => r.ProductId == p.Id && r.Status == StoreSaleRequestStatus.Open)))
            .ToListAsync(ct);

        return Ok(ListPaging.Apply(rows, page, pageSize, Response));
    }

    /// <summary>Everybody who can be named as an item's seller: the holders of the Seller role.</summary>
    [HttpGet("sellers")]
    public async Task<ActionResult<IEnumerable<StoreSellerRecord>>> Sellers(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = SellerIds(db);
        var sellers = await db.AppUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new StoreSellerRecord(u.Id, u.DisplayName ?? u.Email ?? u.UserName ?? "Unnamed", u.Email))
            .ToListAsync(ct);
        // Sorted here: a member of a record built in the projection has no SQL to order by.
        return Ok(sellers.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    public const string NotASeller = StoreProductEditor.NotASeller;

    private static IQueryable<Guid> SellerIds(BenDataContext db) => StoreProductEditor.SellerIds(db);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await StoreProductRecords.LoadAsync(db, id, ct) is { } record ? Ok(record) : NotFound();
    }

    /// <summary>A new product from just a name: hidden, with one $0.00 variant to price.</summary>
    [HttpPost]
    public async Task<ActionResult<StoreProductAdminRecord>> Create(
        [FromBody] CreateStoreProductRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (product, refusal) = await _editor.CreateAsync(db, request.Name, request.CategoryId, me, ct);
        if (refusal is not null) return this.Refused(refusal);
        await TryAuditAsync(auditLog.LogCreateAsync(nameof(StoreProduct), product!.Id, product, me.UserId, AppSources.WebApi));
        return Ok(await StoreProductRecords.LoadAsync(db, product.Id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> Update(
        Guid id, [FromBody] SaveStoreProductRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        var before = Clone(product);

        var refusal = await _editor.SaveDetailsAsync(db, product, new StoreDetailsEdit(
            request.Name, request.CategoryId, request.ShortDescription, request.LongDescriptionHtml, request.Specs,
            request.ExpectedDateUpdated,
            new StoreAdminDetailsEdit(request.Slug, request.EquipmentModelId, request.IsFeatured, request.NewUntilUtc,
                request.StripeTaxCode, request.SortOrder, request.SellerAppUserId)), me, ct);
        if (refusal is not null) return this.Refused(refusal);

        await TryAuditAsync(auditLog.LogUpdateAsync(nameof(StoreProduct), id, before, product, me.UserId, AppSources.WebApi));
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    /// <summary>
    /// Puts a product on sale — once it has a price, a picture, a live variant and a visible
    /// category. A seller's item goes on sale only through their request (the sale-request queue),
    /// which is what records what the seller is paid.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<StoreProductAdminRecord>> Activate(Guid id, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (product.SellerAppUserId is not null && !product.IsActive)
            return BadRequest(SellersItemGoesOnSaleByRequest);
        return await SwitchAsync(db, product, on: true, me, ct);
    }

    public const string SellersItemGoesOnSaleByRequest =
        "A seller's item goes on sale when its seller asks — approve their request on the Sale requests page, which records what they're paid.";

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<StoreProductAdminRecord>> Deactivate(Guid id, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : await SwitchAsync(db, product, on: false, me, ct);
    }

    /// <summary>A hidden copy — options, variants (new SKUs, no stock), specs and pictures (new files).</summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<StoreProductAdminRecord>> Duplicate(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var source = await db.StoreProducts.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.Name }).FirstOrDefaultAsync(ct);
        if (source is null) return NotFound();

        var now = DateTime.UtcNow;
        var copy = await StoreProductCopier.CopyAsync(db, images, id,
            new StoreProductCopier.Plan($"{source.Name} (copy)", "-COPY", Everything: false, SellerAppUserId: null), userId, now, ct);
        if (copy is null) return NotFound();
        StoreProductHistory.Record(db, copy.Id, StoreProductChangeArea.Created, $"Copied it from “{source.Name}”.",
            userId, StoreChangeActor.Store, now);
        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, copy.Id, ct);
        await TryAuditAsync(auditLog.LogCreateAsync(nameof(StoreProduct), copy.Id, copy, userId, AppSources.WebApi));
        return Ok(await StoreProductRecords.LoadAsync(db, copy.Id, ct));
    }

    /// <summary>Removes a product nobody has bought. One that has been sold is switched off instead.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await _editor.DeleteAsync(db, product, me, ct) is { } refusal) return this.Refused(refusal);
        await TryAuditAsync(auditLog.LogDeleteAsync(nameof(StoreProduct), id, product, me.UserId, AppSources.WebApi));
        return NoContent();
    }

    // ── options and variants ─────────────────────────────────────────────────

    /// <summary>Replaces a product's options with this list. A value a variant still uses cannot go.</summary>
    [HttpPut("{id:guid}/options")]
    public async Task<ActionResult<StoreProductAdminRecord>> SaveOptions(
        Guid id, [FromBody] SaveStoreOptionsRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await _editor.SaveOptionsAsync(db, id, request.Options, request.ExpectedDateUpdated, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    /// <summary>
    /// Adds a variant for every combination of active option values the product lacks. The
    /// option-less default goes once real combinations exist — unless it has been sold or holds stock.
    /// </summary>
    [HttpPost("{id:guid}/variants/generate")]
    public async Task<ActionResult<StoreVariantsGenerated>> GenerateVariants(Guid id, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (added, refusal) = await _editor.GenerateVariantsAsync(db, id, me, ct);
        if (refusal is not null) return this.Refused(refusal);
        var record = await StoreProductRecords.LoadAsync(db, id, ct);
        return Ok(new StoreVariantsGenerated(added, record!.Variants));
    }

    [HttpPost("{id:guid}/variants")]
    public async Task<ActionResult<StoreProductAdminRecord>> CreateVariant(
        Guid id, [FromBody] SaveStoreVariantRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await _editor.CreateVariantAsync(db, product, Edit(request), me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> UpdateVariant(
        Guid id, Guid variantId, [FromBody] SaveStoreVariantRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await _editor.UpdateVariantAsync(db, product, variantId, Edit(request), me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> DeleteVariant(Guid id, Guid variantId, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await _editor.DeleteVariantAsync(db, product, variantId, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    private static StoreVariantEdit Edit(SaveStoreVariantRequest r)
        => new(r.Sku, r.Price, r.CompareAtPrice, r.IsActive, r.IsDefault, r.SortOrder, r.OptionValueIds, r.InitialStock);

    // ── stock ────────────────────────────────────────────────────────────────

    /// <summary>A change or a counted quantity for one variant; it leaves a movement row.</summary>
    [HttpPost("{id:guid}/variants/{variantId:guid}/stock")]
    public async Task<ActionResult<StoreProductAdminRecord>> AdjustStock(
        Guid id, Guid variantId, [FromBody] AdjustStockRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await StoreProductEditor.AdjustStockAsync(db, id, variantId, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    [HttpGet("{id:guid}/variants/{variantId:guid}/stock")]
    public async Task<ActionResult<IEnumerable<StoreStockMovementRecord>>> StockLog(Guid id, Guid variantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProductVariants.AnyAsync(v => v.Id == variantId && v.ProductId == id, ct)) return NotFound();

        return Ok(await db.StoreStockMovements.AsNoTracking()
            .Where(m => m.VariantId == variantId)
            .OrderByDescending(m => m.OccurredUtc)
            .Select(m => new StoreStockMovementRecord(
                m.Id, m.Delta, m.QuantityAfter, m.Reason, m.Note, m.OrderId,
                db.StoreOrders.Where(o => o.Id == m.OrderId).Select(o => (int?)o.OrderNumber).FirstOrDefault(),
                m.OccurredUtc,
                db.AppUsers.Where(u => u.Id == m.ActorAppUserId).Select(u => u.DisplayName ?? u.UserName).FirstOrDefault()))
            .ToListAsync(ct));
    }

    // ── history ──────────────────────────────────────────────────────────────

    /// <summary>Who changed what, newest first (store sellers P2) — every line, with names.</summary>
    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IEnumerable<StoreProductChangeRecord>>> History(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        return Ok(ListPaging.Apply(await StoreProductHistory.ReadAsync(db, id, forSeller: null, ct), page, pageSize, Response));
    }

    /// <summary>Why a stock request cannot be applied as asked, or null. Shared with the stock page.</summary>
    internal static string? StockRequestProblem(int? delta, int? setTo, StoreStockReason reason)
        => StoreProductEditor.StockRequestProblem(delta, setTo, reason);

    // ── pictures ─────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<StoreProductAdminRecord>> AddImage(
        Guid id, IFormFile? file, [FromForm] string? altText, [FromForm] Guid? variantId, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");

        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);
        if (await _editor.AddImageAsync(db, id, buffer.ToArray(), file.ContentType, file.FileName, altText, variantId, me, ct) is { } refusal)
            return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/images/reorder")]
    public async Task<ActionResult<StoreProductAdminRecord>> ReorderImages(
        Guid id, [FromBody] ReorderRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await StoreProductEditor.ReorderImagesAsync(db, id, request.OrderedIds, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> UpdateImage(
        Guid id, Guid imageId, [FromBody] UpdateStoreImageRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await StoreProductEditor.UpdateImageAsync(db, id, imageId, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await _editor.DeleteImageAsync(db, product, imageId, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.LoadAsync(db, id, ct));
    }

    // ── parts and cost (store sellers P4) ────────────────────────────────────

    /// <summary>What a sale of this product is worth to each side (store sellers P9) — never a shopper's to see.</summary>
    [HttpGet("{id:guid}/economics")]
    public async Task<ActionResult<StoreProductEconomicsRecord>> Economics(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.StoreProducts.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.SellerAppUserId, x.SellerAskPerUnit, x.MinPrice, x.MaxPrice }).FirstOrDefaultAsync(ct);
        if (p is null) return NotFound();
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var cost = (await StoreCostBasisReader.ReadAsync(db, [id], ct)).GetValueOrDefault(id);
        var hasSeller = p.SellerAppUserId is not null;
        var owed = hasSeller ? StoreEconomics.SellerEarning(true, cost, p.SellerAskPerUnit) : cost;
        decimal? price = p.MaxPrice > 0 ? p.MinPrice : null;
        var fee = price is { } at ? StoreEconomics.EstimatedFee(at, settings.FeePercent, settings.FeeFixed) : 0m;
        return Ok(new StoreProductEconomicsRecord(
            cost, hasSeller, p.SellerAskPerUnit, owed, price, p.MaxPrice > 0 ? p.MaxPrice : null, fee,
            price is { } priced ? StoreMoney.Round(priced - owed - fee) : null,
            StoreEconomics.SuggestedPrice(owed, settings.MarkupPercent, settings.FeePercent, settings.FeeFixed),
            price is { } check && StoreEconomics.BelowCost(check, owed, settings.FeePercent, settings.FeeFixed),
            settings.MarkupPercent, settings.FeePercent, settings.FeeFixed));
    }

    [HttpGet("{id:guid}/parts")]
    public async Task<ActionResult<StorePartsRecord>> Parts(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await StoreProductRecords.PartsAsync(db, id, ct) is { } parts ? Ok(parts) : NotFound();
    }

    [HttpPut("{id:guid}/parts")]
    public async Task<ActionResult<StorePartsRecord>> SaveParts(Guid id, [FromBody] SaveStorePartsRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await _editor.SavePartsAsync(db, product, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    // ── versions (store sellers P13) ─────────────────────────────────────────

    [HttpGet("{id:guid}/version")]
    public async Task<ActionResult<StoreVersionInfo>> Version(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : Ok(await StoreProductVersions.InfoAsync(db, product, ct));
    }

    [HttpPut("{id:guid}/version")]
    public async Task<ActionResult<StoreVersionInfo>> SaveVersion(Guid id, [FromBody] SaveStoreVersionRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await StoreProductEditor.SaveVersionAsync(db, product, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductVersions.InfoAsync(db, product, ct));
    }

    [HttpPost("{id:guid}/new-version")]
    public async Task<ActionResult<StoreNewVersionRecord>> StartVersion(Guid id, [FromBody] StartStoreVersionRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        var (newId, refusal) = await _editor.StartVersionAsync(db, product, request, me, ct);
        return refusal is not null ? this.Refused(refusal) : Ok(new StoreNewVersionRecord(newId!.Value));
    }

    // ── FAQ (store sellers P12) ──────────────────────────────────────────────

    [HttpGet("{id:guid}/faqs")]
    public async Task<ActionResult<StoreFaqsRecord>> Faqs(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await StoreProductRecords.FaqsAsync(db, id, ct) is { } faqs ? Ok(faqs) : NotFound();
    }

    [HttpPut("{id:guid}/faqs")]
    public async Task<ActionResult<StoreFaqsRecord>> SaveFaqs(Guid id, [FromBody] SaveStoreFaqsRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await StoreProductEditor.SaveFaqsAsync(db, product, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FaqsAsync(db, id, ct));
    }

    [HttpPost("{id:guid}/parts/{partId:guid}/picture")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<StorePartsRecord>> SetPartPicture(Guid id, Guid partId, IFormFile? file, CancellationToken ct)
    {
        var me = Me;
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);
        if (await _editor.SetPartPictureAsync(db, id, partId, buffer.ToArray(), file.ContentType, file.FileName, me, ct) is { } refusal)
            return this.Refused(refusal);
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/parts/{partId:guid}/picture")]
    public async Task<ActionResult<StorePartsRecord>> RemovePartPicture(Guid id, Guid partId, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await _editor.RemovePartPictureAsync(db, id, partId, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    // ── files (store sellers P11) ────────────────────────────────────────────

    [HttpGet("{id:guid}/files")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> Files(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpPost("{id:guid}/files")]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> AddFile(Guid id, IFormFile? file, [FromForm] string? title,
        [FromForm] StoreProductFileKind kind, [FromForm] StoreFileAudience audience, [FromForm] string? versionLabel, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("There was no file in that upload.");
        await using var stream = file.OpenReadStream();
        if (await _editor.AddFileAsync(db, id, stream, file.Length, file.ContentType, file.FileName,
                new SaveStoreProductFileRequest(title ?? "", kind, audience, versionLabel, 0), me, ct) is { } refusal)
            return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpPost("{id:guid}/files/manual")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> AddManual(Guid id, [FromBody] SaveStoreManualRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await _editor.AddManualAsync(db, id, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/files/{fileId:guid}")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> SaveFile(Guid id, Guid fileId, [FromBody] SaveStoreProductFileRequest request, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await StoreProductEditor.UpdateFileAsync(db, id, fileId, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/files/{fileId:guid}")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> DeleteFile(Guid id, Guid fileId, CancellationToken ct)
    {
        var me = Me;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await _editor.DeleteFileAsync(db, id, fileId, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private async Task<ActionResult<StoreProductAdminRecord>> SwitchAsync(
        BenDataContext db, StoreProduct product, bool on, StoreEditActor me, CancellationToken ct)
    {
        var before = Clone(product);
        var was = product.IsActive;
        if (await _editor.SwitchAsync(db, product, on, me, ct) is { } refusal) return this.Refused(refusal);
        if (was != on)
            await TryAuditAsync(auditLog.LogUpdateAsync(nameof(StoreProduct), product.Id, before, product, me.UserId, AppSources.WebApi));
        return Ok(await StoreProductRecords.LoadAsync(db, product.Id, ct));
    }

    /// <summary>Same-type copy for the audit diff — the tracker refuses anonymous objects.</summary>
    private static StoreProduct Clone(StoreProduct p) => new()
    {
        Id = p.Id, CategoryId = p.CategoryId, EquipmentModelId = p.EquipmentModelId, Name = p.Name, Slug = p.Slug,
        ShortDescription = p.ShortDescription, LongDescriptionHtml = p.LongDescriptionHtml, IsActive = p.IsActive,
        IsFeatured = p.IsFeatured, SortOrder = p.SortOrder, NewUntilUtc = p.NewUntilUtc, StripeTaxCode = p.StripeTaxCode,
        SellerAppUserId = p.SellerAppUserId,
    };
}
