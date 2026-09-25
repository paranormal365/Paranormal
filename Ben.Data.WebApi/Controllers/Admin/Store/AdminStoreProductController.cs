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

        var source = await db.StoreProducts.AsNoTracking()
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .Include(p => p.Specs).Include(p => p.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (source is null) return NotFound();

        var now = DateTime.UtcNow;
        var copy = new StoreProduct
        {
            Id = Guid.NewGuid(), CategoryId = source.CategoryId, EquipmentModelId = source.EquipmentModelId,
            Name = StoreProductEditor.Truncate($"{source.Name} (copy)", MaxNameLength), ShortDescription = source.ShortDescription,
            LongDescriptionHtml = source.LongDescriptionHtml, IsActive = false, IsFeatured = false,
            SortOrder = source.SortOrder + 1, StripeTaxCode = source.StripeTaxCode,
            DateCreated = now, CreatedByAppUserId = userId,
        };
        copy.Slug = await StoreSlugs.ForProductAsync(db, copy.Id, copy.Name, ct);
        db.StoreProducts.Add(copy);

        var valueMap = new Dictionary<Guid, Guid>();
        foreach (var option in source.Options)
        {
            var newOption = new StoreProductOption
            {
                Id = Guid.NewGuid(), ProductId = copy.Id, Name = option.Name, Kind = option.Kind,
                SortOrder = option.SortOrder, DateCreated = now, CreatedByAppUserId = userId,
            };
            db.StoreProductOptions.Add(newOption);
            foreach (var value in option.Values)
            {
                valueMap[value.Id] = Guid.NewGuid();
                db.StoreProductOptionValues.Add(new StoreProductOptionValue
                {
                    Id = valueMap[value.Id], OptionId = newOption.Id, Value = value.Value, SwatchHex = value.SwatchHex,
                    SortOrder = value.SortOrder, IsActive = value.IsActive, DateCreated = now, CreatedByAppUserId = userId,
                });
            }
        }

        var variantMap = new Dictionary<Guid, Guid>();
        var takenSkus = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in source.Variants)
        {
            variantMap[variant.Id] = Guid.NewGuid();
            var sku = await StoreProductEditor.UniqueSkuAsync(db, StoreProductEditor.Truncate(variant.Sku, 58) + "-COPY", ct, takenSkus);
            takenSkus.Add(sku);
            var values = variant.OptionValues.Select(x => valueMap[x.OptionValueId]).ToList();
            db.StoreProductVariants.Add(new StoreProductVariant
            {
                Id = variantMap[variant.Id], ProductId = copy.Id, Sku = sku, Name = variant.Name,
                OptionSignature = StorePriceCaches.Signature(values), Price = variant.Price,
                CompareAtPrice = variant.CompareAtPrice, IsActive = variant.IsActive, IsDefault = variant.IsDefault,
                SortOrder = variant.SortOrder, DateCreated = now, CreatedByAppUserId = userId,
            });
            foreach (var valueId in values)
                db.StoreProductVariantOptionValues.Add(new StoreProductVariantOptionValue
                {
                    Id = Guid.NewGuid(), VariantId = variantMap[variant.Id], OptionValueId = valueId, DateCreated = now,
                });
        }

        foreach (var spec in source.Specs)
            db.StoreProductSpecs.Add(new StoreProductSpec
            {
                Id = Guid.NewGuid(), ProductId = copy.Id, GroupName = spec.GroupName, Name = spec.Name,
                Value = spec.Value, SortOrder = spec.SortOrder, DateCreated = now, CreatedByAppUserId = userId,
            });

        foreach (var picture in source.Images.OrderBy(i => i.SortOrder))
        {
            var file = await images.CopyAsync(db, picture.UploadFileId, $"products/{copy.Id:N}", userId, ct);
            db.StoreProductImages.Add(new StoreProductImage
            {
                Id = Guid.NewGuid(), ProductId = copy.Id, UploadFileId = file.Id, SortOrder = picture.SortOrder,
                AltText = picture.AltText,
                VariantId = picture.VariantId is { } v ? variantMap[v] : null,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }

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
