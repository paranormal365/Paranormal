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
    public const int MaxNameLength = 200;
    public const int MaxShortDescriptionLength = 500;
    public const int MaxLongDescriptionLength = 20_000;

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
                p.SellerAppUser == null ? null : p.SellerAppUser.DisplayName ?? p.SellerAppUser.Email))
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

    public const string NotASeller = "That person isn't a seller. Give them the Seller role on their Site Roles tab first.";

    /// <summary>The Seller role's holders, as a query.</summary>
    private static IQueryable<Guid> SellerIds(BenDataContext db)
        => db.UserRoles.Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.Seller)).Select(ur => ur.UserId);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await LoadAsync(db, id, ct) is { } record ? Ok(record) : NotFound();
    }

    /// <summary>A new product from just a name: hidden, with one $0.00 variant to price.</summary>
    [HttpPost]
    public async Task<ActionResult<StoreProductAdminRecord>> Create(
        [FromBody] CreateStoreProductRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest("A product needs a name.");
        if (name.Length > MaxNameLength) return BadRequest($"A product name is {MaxNameLength} characters at most.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var categoryId = request.CategoryId
            ?? await db.StoreCategories.OrderBy(c => c.SortOrder).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (categoryId is null || !await db.StoreCategories.AnyAsync(c => c.Id == categoryId, ct))
            return BadRequest("Add a category first — every product is filed under one.");

        var now = DateTime.UtcNow;
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), CategoryId = categoryId.Value, Name = name, IsActive = false,
            SortOrder = (await db.StoreProducts.Where(p => p.CategoryId == categoryId)
                .MaxAsync(p => (int?)p.SortOrder, ct) ?? -1) + 1,
            DateCreated = now, CreatedByAppUserId = userId,
        };
        product.Slug = await StoreSlugs.ForProductAsync(db, product.Id, name, ct);
        db.StoreProducts.Add(product);
        db.StoreProductVariants.Add(new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Sku = await UniqueSkuAsync(db, product.Slug, ct),
            OptionSignature = string.Empty, Price = 0m, IsActive = true, IsDefault = true,
            DateCreated = now, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);
        await TryAuditAsync(auditLog.LogCreateAsync(nameof(StoreProduct), product.Id, product, userId, AppSources.WebApi));

        return Ok(await LoadAsync(db, product.Id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> Update(
        Guid id, [FromBody] SaveStoreProductRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var product = await db.StoreProducts.Include(p => p.Specs).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        var before = Clone(product);

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest("A product needs a name.");
        if (name.Length > MaxNameLength) return BadRequest($"A product name is {MaxNameLength} characters at most.");
        if (!await db.StoreCategories.AnyAsync(c => c.Id == request.CategoryId, ct))
            return BadRequest("That category no longer exists.");
        if (request.EquipmentModelId is { } modelId && !await db.EquipmentModels.AnyAsync(m => m.Id == modelId, ct))
            return BadRequest("That equipment model no longer exists.");
        var shortDescription = Trimmed(request.ShortDescription);
        if (shortDescription?.Length > MaxShortDescriptionLength)
            return BadRequest($"The short description is {MaxShortDescriptionLength} characters at most.");
        var taxCode = Trimmed(request.StripeTaxCode);
        if (taxCode is not null && !TaxCode().IsMatch(taxCode))
            return BadRequest("A Stripe tax code looks like txcd_99999999.");
        if (SpecProblem(request.Specs) is { } badSpec) return BadRequest(badSpec);
        // A seller is named only from the Seller role; one who has since lost it can stay on the
        // item they already had, so an unrelated save does not fail over it.
        if (request.SellerAppUserId is { } sellerId && sellerId != product.SellerAppUserId
            && !await SellerIds(db).AnyAsync(u => u == sellerId, ct))
            return BadRequest(NotASeller);

        if (!string.IsNullOrWhiteSpace(request.Slug))
        {
            var slug = StoreSlugs.Typed(request.Slug);
            if (slug is null) return BadRequest("A web address needs letters or digits in it.");
            if (await db.StoreProducts.AnyAsync(p => p.Id != id && p.Slug == slug, ct))
                return Conflict($"There is already a product at /store/p/{slug}.");
            product.Slug = slug;
        }

        var html = sanitizer.SanitizeHtml(request.LongDescriptionHtml);
        if (html.Length > MaxLongDescriptionLength)
            return BadRequest($"The description is {MaxLongDescriptionLength:N0} characters at most.");

        product.CategoryId = request.CategoryId;
        product.EquipmentModelId = request.EquipmentModelId;
        product.Name = name;
        product.ShortDescription = shortDescription;
        product.LongDescriptionHtml = string.IsNullOrWhiteSpace(html) ? null : html;
        product.IsFeatured = request.IsFeatured;
        product.NewUntilUtc = request.NewUntilUtc;
        product.StripeTaxCode = taxCode;
        product.SortOrder = request.SortOrder;
        product.SellerAppUserId = request.SellerAppUserId;
        product.DateUpdated = DateTime.UtcNow;
        product.UpdatedByAppUserId = userId;

        db.StoreProductSpecs.RemoveRange(product.Specs);
        var order = 0;
        foreach (var group in request.Specs ?? [])
            foreach (var item in group.Items ?? [])
                db.StoreProductSpecs.Add(new StoreProductSpec
                {
                    Id = Guid.NewGuid(), ProductId = id, GroupName = group.GroupName.Trim(),
                    Name = item.Name.Trim(), Value = item.Value.Trim(), SortOrder = order++,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                });

        await db.SaveChangesAsync(ct);
        await TryAuditAsync(auditLog.LogUpdateAsync(nameof(StoreProduct), id, before, product, userId, AppSources.WebApi));
        return Ok(await LoadAsync(db, id, ct));
    }

    /// <summary>Puts a product on sale — once it has a price, a picture, a live variant and a visible category.</summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult<StoreProductAdminRecord>> Activate(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var product = await db.StoreProducts.Include(p => p.Category).ThenInclude(c => c.ParentCategory).Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();

        var live = product.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ToList();
        if (live.FirstOrDefault(v => v.Price <= 0m) is { } unpriced)
            return BadRequest(unpriced.IsDefault && product.Variants.Count == 1
                ? "Price the default variant first — it's still $0.00."
                : $"Price {StorePriceCaches.Label(unpriced.Name)} first — it's still $0.00.");
        if (!await db.StoreProductImages.AnyAsync(i => i.ProductId == id, ct))
            return BadRequest("Add at least one picture before showing it.");
        if (live.Count == 0) return BadRequest("At least one variant must be active.");
        if (!product.Category.IsActive) return BadRequest("Its category is hidden — show the category first.");
        if (product.Category.ParentCategory is { IsActive: false } parent)
            return BadRequest($"Its category sits under {parent.Name}, which is hidden — show {parent.Name} first.");

        return await SwitchAsync(db, product, on: true, userId, ct);
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<ActionResult<StoreProductAdminRecord>> Deactivate(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : await SwitchAsync(db, product, on: false, userId, ct);
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
            Name = Truncate($"{source.Name} (copy)", MaxNameLength), ShortDescription = source.ShortDescription,
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
            var sku = await UniqueSkuAsync(db, Truncate(variant.Sku, 58) + "-COPY", ct, takenSkus);
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

        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, copy.Id, ct);
        await TryAuditAsync(auditLog.LogCreateAsync(nameof(StoreProduct), copy.Id, copy, userId, AppSources.WebApi));
        return Ok(await LoadAsync(db, copy.Id, ct));
    }

    /// <summary>Removes a product nobody has bought. One that has been sold is switched off instead.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (await db.StoreOrderItems.AnyAsync(i => i.ProductId == id, ct))
            return BadRequest("That product has been sold, so its record must stay. Deactivate it instead.");

        var pictures = await db.StoreProductImages.Where(i => i.ProductId == id).Select(i => i.UploadFileId).ToListAsync(ct);

        // Children first on the NoAction paths (a variant's choices point at values that also
        // cascade from the product; a picture's variant likewise), then the product takes the rest.
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await db.StoreProductVariantOptionValues.Where(x => x.Variant.ProductId == id).ExecuteDeleteAsync(ct);
            await db.StoreProductImages.Where(i => i.ProductId == id).ExecuteDeleteAsync(ct);
            await db.StoreProducts.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }

        foreach (var file in pictures) await images.RemoveAsync(db, file, ct);
        await TryAuditAsync(auditLog.LogDeleteAsync(nameof(StoreProduct), id, product, userId, AppSources.WebApi));
        return NoContent();
    }

    // ── options and variants ─────────────────────────────────────────────────

    /// <summary>Replaces a product's options with this list. A value a variant still uses cannot go.</summary>
    [HttpPut("{id:guid}/options")]
    public async Task<ActionResult<StoreProductAdminRecord>> SaveOptions(
        Guid id, [FromBody] SaveStoreOptionsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var product = await db.StoreProducts.Include(p => p.Options).ThenInclude(o => o.Values)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();

        var wanted = request.Options ?? [];
        if (wanted.Count > StoreCatalogRules.MaxOptionsPerProduct)
            return BadRequest("Three options is the most a product can carry.");
        if (OptionsProblem(wanted, product) is { } problem) return BadRequest(problem);

        // A value leaving the product must not be one a variant is made of.
        var kept = wanted.SelectMany(o => o.Values).Where(v => v.Id is not null).Select(v => v.Id!.Value).ToHashSet();
        var leaving = product.Options.SelectMany(o => o.Values).Where(v => !kept.Contains(v.Id)).ToList();
        if (leaving.Count > 0)
        {
            var leavingIds = leaving.Select(v => v.Id).ToList();
            var used = await db.StoreProductVariantOptionValues
                .Where(x => leavingIds.Contains(x.OptionValueId))
                .Select(x => new { x.OptionValueId, x.Variant.Name })
                .FirstOrDefaultAsync(ct);
            if (used is not null)
                return BadRequest($"{leaving.First(v => v.Id == used.OptionValueId).Value} is still used by variant "
                                + $"{StorePriceCaches.Label(used.Name)} — remove it from the variant first.");
        }

        var now = DateTime.UtcNow;
        var keptOptions = wanted.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
        db.StoreProductOptionValues.RemoveRange(leaving);
        db.StoreProductOptions.RemoveRange(product.Options.Where(o => !keptOptions.Contains(o.Id)));

        for (var i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            var option = w.Id is { } oid ? product.Options.Single(o => o.Id == oid) : null;
            if (option is null)
            {
                option = new StoreProductOption { Id = Guid.NewGuid(), ProductId = id, DateCreated = now, CreatedByAppUserId = userId };
                db.StoreProductOptions.Add(option);
            }
            else { option.DateUpdated = now; option.UpdatedByAppUserId = userId; }
            option.Name = w.Name.Trim();
            option.Kind = w.Kind;
            option.SortOrder = i;

            for (var j = 0; j < w.Values.Count; j++)
            {
                var wv = w.Values[j];
                var value = wv.Id is { } vid ? option.Values.Single(v => v.Id == vid) : null;
                if (value is null)
                {
                    value = new StoreProductOptionValue { Id = Guid.NewGuid(), OptionId = option.Id, DateCreated = now, CreatedByAppUserId = userId };
                    db.StoreProductOptionValues.Add(value);
                }
                else { value.DateUpdated = now; value.UpdatedByAppUserId = userId; }
                value.Value = wv.Value.Trim();
                value.SwatchHex = w.Kind == StoreOptionKind.Swatch ? wv.SwatchHex!.Trim().ToLowerInvariant() : null;
                value.IsActive = wv.IsActive;
                value.SortOrder = j;
            }
        }

        product.DateUpdated = now;
        product.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, id, ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    /// <summary>
    /// Adds a variant for every combination of active option values the product lacks. The
    /// option-less default goes once real combinations exist — unless it has been sold or holds stock.
    /// </summary>
    [HttpPost("{id:guid}/variants/generate")]
    public async Task<ActionResult<StoreVariantsGenerated>> GenerateVariants(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        var options = await db.StoreProductOptions.AsNoTracking().Where(o => o.ProductId == id)
            .OrderBy(o => o.SortOrder)
            .Select(o => o.Values.Where(v => v.IsActive).OrderBy(v => v.SortOrder).Select(v => new { v.Id, v.Value }).ToList())
            .ToListAsync(ct);
        if (options.Count == 0 || options.Any(o => o.Count == 0))
            return BadRequest("Give every option at least one value first.");

        var variants = await db.StoreProductVariants.Where(v => v.ProductId == id).ToListAsync(ct);
        var existing = variants.Select(v => v.OptionSignature).ToHashSet(StringComparer.Ordinal);
        var template = variants.OrderByDescending(v => v.IsDefault).ThenBy(v => v.SortOrder).FirstOrDefault();

        var combinations = options.Aggregate(
            new[] { Array.Empty<(Guid Id, string Value)>() }.AsEnumerable(),
            (acc, values) => acc.SelectMany(prefix => values.Select(v => prefix.Append((v.Id, v.Value)).ToArray())));

        var now = DateTime.UtcNow;
        var added = 0;
        var order = variants.Count == 0 ? 0 : variants.Max(v => v.SortOrder) + 1;
        var takenSkus = new HashSet<string>(StringComparer.Ordinal);
        foreach (var combo in combinations)
        {
            var signature = StorePriceCaches.Signature(combo.Select(c => c.Id));
            if (!existing.Add(signature)) continue;

            var sku = await UniqueSkuAsync(db, $"{product.Slug}-{string.Join('-', combo.Select(c => UrlSlug.From(c.Value) ?? "x"))}", ct, takenSkus);
            takenSkus.Add(sku);
            var variant = new StoreProductVariant
            {
                Id = Guid.NewGuid(), ProductId = id, Sku = sku, OptionSignature = signature,
                Price = template?.Price ?? 0m, IsActive = true, SortOrder = order++,
                DateCreated = now, CreatedByAppUserId = userId,
            };
            db.StoreProductVariants.Add(variant);
            foreach (var (valueId, _) in combo)
                db.StoreProductVariantOptionValues.Add(new StoreProductVariantOptionValue
                {
                    Id = Guid.NewGuid(), VariantId = variant.Id, OptionValueId = valueId, DateCreated = now,
                });
            variants.Add(variant);
            added++;
        }

        // The option-less default stands for "this product has no choices". Once it has, the
        // default goes — or, if it has been sold or holds stock, is switched off and kept.
        if (added > 0 && variants.FirstOrDefault(v => v.OptionSignature.Length == 0) is { } bare)
        {
            var ordered = await db.StoreOrderItems.AnyAsync(i => i.VariantId == bare.Id, ct);
            if (ordered || bare.StockOnHand > 0 || bare.StockReserved > 0) bare.IsActive = false;
            else
            {
                await db.StoreProductImages.Where(i => i.VariantId == bare.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.VariantId, (Guid?)null), ct);
                db.StoreProductVariants.Remove(bare);
                variants.Remove(bare);
            }
            bare.IsDefault = false;
            if (!variants.Any(v => v.IsDefault && v.IsActive))
                variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).First().IsDefault = true;
        }

        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, id, ct);
        var record = await LoadAsync(db, id, ct);
        return Ok(new StoreVariantsGenerated(added, record!.Variants));
    }

    [HttpPost("{id:guid}/variants")]
    public async Task<ActionResult<StoreProductAdminRecord>> CreateVariant(
        Guid id, [FromBody] SaveStoreVariantRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (product is null) return NotFound();
        if (request.InitialStock is < 0) return BadRequest("Starting stock can't be negative.");

        var variant = new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = id, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        if (await ApplyVariantAsync(db, product, variant, request, isNew: true, ct) is { } refused) return refused;
        db.StoreProductVariants.Add(variant);
        await db.SaveChangesAsync(ct);

        if (request.InitialStock is > 0 and var stock)
            await StoreStock.AdjustAsync(db, variant.Id, stock, StoreStockReason.Received, "Starting stock", userId, DateTime.UtcNow, ct);

        await StorePriceCaches.RecomputeAsync(db, id, ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> UpdateVariant(
        Guid id, Guid variantId, [FromBody] SaveStoreVariantRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        var variant = await db.StoreProductVariants.Include(v => v.OptionValues)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == id, ct);
        if (product is null || variant is null) return NotFound();

        if (product.IsActive && variant.IsActive && !request.IsActive
            && !await db.StoreProductVariants.AnyAsync(v => v.ProductId == id && v.Id != variantId && v.IsActive, ct))
            return BadRequest(LastLiveVariant);

        if (await ApplyVariantAsync(db, product, variant, request, isNew: false, ct) is { } refused) return refused;
        variant.DateUpdated = DateTime.UtcNow;
        variant.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        await StorePriceCaches.RecomputeAsync(db, id, ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> DeleteVariant(Guid id, Guid variantId, CancellationToken ct)
    {
        GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.FirstOrDefaultAsync(p => p.Id == id, ct);
        var variant = await db.StoreProductVariants.FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == id, ct);
        if (product is null || variant is null) return NotFound();

        if (await db.StoreOrderItems.AnyAsync(i => i.VariantId == variantId, ct))
            return BadRequest("That variant has been ordered — deactivate it instead.");
        var others = await db.StoreProductVariants.Where(v => v.ProductId == id && v.Id != variantId).ToListAsync(ct);
        if (others.Count == 0) return BadRequest("A product keeps at least one variant.");
        if (product.IsActive && variant.IsActive && !others.Any(v => v.IsActive)) return BadRequest(LastLiveVariant);

        await db.StoreProductImages.Where(i => i.VariantId == variantId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.VariantId, (Guid?)null), ct);
        db.StoreProductVariants.Remove(variant);
        if (variant.IsDefault) (others.FirstOrDefault(v => v.IsActive) ?? others[0]).IsDefault = true;
        await db.SaveChangesAsync(ct);

        await StorePriceCaches.RecomputeAsync(db, id, ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    // ── stock ────────────────────────────────────────────────────────────────

    /// <summary>A change or a counted quantity for one variant; it leaves a movement row.</summary>
    [HttpPost("{id:guid}/variants/{variantId:guid}/stock")]
    public async Task<ActionResult<StoreProductAdminRecord>> AdjustStock(
        Guid id, Guid variantId, [FromBody] AdjustStockRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (StockRequestProblem(request.Delta, request.SetTo, request.Reason) is { } problem) return BadRequest(problem);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProductVariants.AnyAsync(v => v.Id == variantId && v.ProductId == id, ct)) return NotFound();

        var refusal = await StoreStock.AdjustManyAsync(db, [new StoreStockChange(variantId, request.Delta, request.SetTo)],
            request.Reason, request.Note, userId, DateTime.UtcNow, ct);
        if (refusal is not null) return BadRequest(refusal);

        return Ok(await LoadAsync(db, id, ct));
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

    /// <summary>Why a stock request cannot be applied as asked, or null. Shared with the stock page.</summary>
    internal static string? StockRequestProblem(int? delta, int? setTo, StoreStockReason reason)
    {
        if (delta is not null && setTo is not null) return "Give either a change or a new quantity, not both.";
        if (delta is null or 0 && setTo is null) return "Give a change or a new quantity.";
        if (setTo is < 0) return "A count can't be below zero.";
        return reason is StoreStockReason.Received or StoreStockReason.Correction or StoreStockReason.Damaged
            ? null
            : $"'{reason}' is written by the system, not by hand.";
    }

    // ── pictures ─────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<StoreProductAdminRecord>> AddImage(
        Guid id, IFormFile? file, [FromForm] string? altText, [FromForm] Guid? variantId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreProducts.AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");
        if (await db.StoreProductImages.CountAsync(i => i.ProductId == id, ct) >= StoreCatalogRules.MaxImagesPerProduct)
            return BadRequest($"This product already has {StoreCatalogRules.MaxImagesPerProduct} pictures.");
        if (variantId is { } v && !await db.StoreProductVariants.AnyAsync(x => x.Id == v && x.ProductId == id, ct))
            return BadRequest("That variant belongs to another product.");

        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);
        var (stored, refusal) = await images.SaveAsync(db, buffer.ToArray(), file.ContentType, file.FileName,
            $"products/{id:N}", userId, ct);
        if (refusal == StoreImageRefusal.NotAPicture) return BadRequest("A product picture is a photograph — JPEG, PNG or similar.");
        if (stored is null) return BadRequest("That picture could not be read.");

        db.StoreProductImages.Add(new StoreProductImage
        {
            Id = Guid.NewGuid(), ProductId = id, UploadFileId = stored.Id, VariantId = variantId,
            AltText = TruncateOrNull(Trimmed(altText), 200),
            SortOrder = (await db.StoreProductImages.Where(i => i.ProductId == id).MaxAsync(i => (int?)i.SortOrder, ct) ?? -1) + 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/images/reorder")]
    public async Task<ActionResult<StoreProductAdminRecord>> ReorderImages(
        Guid id, [FromBody] ReorderRequest request, CancellationToken ct)
    {
        GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pictures = await db.StoreProductImages.Where(i => i.ProductId == id).ToListAsync(ct);
        var ordered = request.OrderedIds ?? [];
        if (ordered.Count != pictures.Count || !pictures.All(p => ordered.Contains(p.Id)))
            return BadRequest("That isn't the full list of pictures.");

        foreach (var picture in pictures) picture.SortOrder = IndexOf(ordered, picture.Id);
        await db.SaveChangesAsync(ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> UpdateImage(
        Guid id, Guid imageId, [FromBody] UpdateStoreImageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var picture = await db.StoreProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == id, ct);
        if (picture is null) return NotFound();
        if (request.VariantId is { } v && !await db.StoreProductVariants.AnyAsync(x => x.Id == v && x.ProductId == id, ct))
            return BadRequest("That variant belongs to another product.");
        var alt = Trimmed(request.AltText);
        if (alt?.Length > 200) return BadRequest("A picture's description is 200 characters at most.");

        picture.AltText = alt;
        picture.VariantId = request.VariantId;
        picture.DateUpdated = DateTime.UtcNow;
        picture.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<StoreProductAdminRecord>> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        var picture = await db.StoreProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == id, ct);
        if (product is null || picture is null) return NotFound();
        if (product.IsActive && !await db.StoreProductImages.AnyAsync(i => i.ProductId == id && i.Id != imageId, ct))
            return BadRequest("A live product keeps at least one picture. Add another first, or take it off sale.");

        db.StoreProductImages.Remove(picture);
        await db.SaveChangesAsync(ct);
        await images.RemoveAsync(db, picture.UploadFileId, ct);
        return Ok(await LoadAsync(db, id, ct));
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private const string LastLiveVariant =
        "The last live variant can't be deactivated while the product is live — take the product off sale first.";

    [GeneratedRegex("^txcd_[0-9]{8}$")]
    private static partial Regex TaxCode();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Hex();

    private async Task<ActionResult<StoreProductAdminRecord>> SwitchAsync(
        BenDataContext db, StoreProduct product, bool on, Guid userId, CancellationToken ct)
    {
        if (product.IsActive != on)
        {
            var before = Clone(product);
            product.IsActive = on;
            product.DateUpdated = DateTime.UtcNow;
            product.UpdatedByAppUserId = userId;
            await db.SaveChangesAsync(ct);
            await TryAuditAsync(auditLog.LogUpdateAsync(nameof(StoreProduct), product.Id, before, product, userId, AppSources.WebApi));
        }
        await StorePriceCaches.RecomputeAsync(db, product.Id, ct);
        return Ok(await LoadAsync(db, product.Id, ct));
    }

    /// <summary>Copies a variant request onto the row, or answers why not.</summary>
    private async Task<ActionResult?> ApplyVariantAsync(
        BenDataContext db, StoreProduct product, StoreProductVariant variant, SaveStoreVariantRequest request,
        bool isNew, CancellationToken ct)
    {
        var sku = request.Sku?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sku)) return BadRequest("A variant needs a SKU.");
        if (sku.Length > 64) return BadRequest("A SKU is 64 characters at most.");
        if (request.Price < 0m) return BadRequest("A price can't be negative.");
        if (request.Price != StoreMoney.Round(request.Price) || request.CompareAtPrice is { } c && c != StoreMoney.Round(c))
            return BadRequest("Prices are dollars and cents — two decimal places at most.");
        if (request.CompareAtPrice is { } old && old <= request.Price)
            return BadRequest("The old price has to be higher than the price.");
        if (product.IsActive && request.IsActive && request.Price <= 0m)
            return BadRequest("This product is on sale, so the variant needs a price above $0.00.");

        var clash = await db.StoreProductVariants.AsNoTracking()
            .Where(v => v.Id != variant.Id && v.Sku == sku)
            .Select(v => new { Product = v.Product.Name, v.Name })
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
            return Conflict($"SKU {sku} is already used by {clash.Product} — {StorePriceCaches.Label(clash.Name)}.");

        // One value for each of the product's options, and only its own.
        var options = await db.StoreProductOptions.AsNoTracking().Where(o => o.ProductId == product.Id)
            .OrderBy(o => o.SortOrder)
            .Select(o => new { o.Id, o.Name, Values = o.Values.Select(v => new { v.Id, v.Value }).ToList() })
            .ToListAsync(ct);
        var chosen = (request.OptionValueIds ?? []).Distinct().ToList();
        var byOption = options.Select(o => (o.Name, Picked: o.Values.Where(v => chosen.Contains(v.Id)).ToList())).ToList();
        var missing = byOption.Where(o => o.Picked.Count != 1).Select(o => o.Name).ToList();
        if (missing.Count > 0) return BadRequest($"Pick one value for each option: {string.Join(", ", missing)}.");
        if (chosen.Count != byOption.Sum(o => o.Picked.Count))
            return BadRequest("One of those choices belongs to another product.");

        var signature = StorePriceCaches.Signature(chosen);
        if (await db.StoreProductVariants.AnyAsync(v => v.ProductId == product.Id && v.Id != variant.Id && v.OptionSignature == signature, ct))
        {
            var label = byOption.Count == 0 ? StorePriceCaches.DefaultLabel : string.Join(" / ", byOption.Select(o => o.Picked[0].Value));
            return BadRequest($"A variant with {label} already exists.");
        }

        variant.Sku = sku;
        variant.Price = request.Price;
        variant.CompareAtPrice = request.CompareAtPrice;
        variant.IsActive = request.IsActive;
        variant.SortOrder = request.SortOrder;
        variant.OptionSignature = signature;

        if (!isNew)
        {
            var current = variant.OptionValues.Select(x => x.OptionValueId).ToHashSet();
            db.StoreProductVariantOptionValues.RemoveRange(variant.OptionValues.Where(x => !chosen.Contains(x.OptionValueId)));
            chosen = chosen.Where(id => !current.Contains(id)).ToList();
        }
        foreach (var valueId in chosen)
            db.StoreProductVariantOptionValues.Add(new StoreProductVariantOptionValue
            {
                Id = Guid.NewGuid(), VariantId = variant.Id, OptionValueId = valueId, DateCreated = DateTime.UtcNow,
            });

        if (request.IsDefault && !variant.IsDefault)
        {
            await db.StoreProductVariants.Where(v => v.ProductId == product.Id && v.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDefault, false), ct);
            variant.IsDefault = true;
        }
        return null;
    }

    private static string? OptionsProblem(IReadOnlyList<SaveStoreOptionRequest> wanted, StoreProduct product)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in wanted)
        {
            var name = option.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return "Each option needs a name, like Colour or Size.";
            if (name.Length > 60) return "An option's name is 60 characters at most.";
            if (!names.Add(name)) return $"There are two options called {name}.";
            if (option.Id is { } oid && product.Options.All(o => o.Id != oid)) return "That option belongs to another product.";

            var values = option.Values ?? [];
            if (values.Count == 0) return $"{name} needs at least one value.";
            if (values.Count > StoreCatalogRules.MaxValuesPerOption)
                return $"An option carries {StoreCatalogRules.MaxValuesPerOption} values at most.";

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var mine = product.Options.FirstOrDefault(o => o.Id == option.Id)?.Values.Select(v => v.Id).ToHashSet() ?? [];
            foreach (var value in values)
            {
                var text = value.Value?.Trim();
                if (string.IsNullOrEmpty(text)) return $"Each value of {name} needs a name.";
                if (text.Length > 60) return "A value is 60 characters at most.";
                if (!seen.Add(text)) return $"{name} lists {text} twice.";
                if (value.Id is { } vid && !mine.Contains(vid)) return "That value belongs to another option.";
                if (option.Kind == StoreOptionKind.Swatch && (value.SwatchHex is null || !Hex().IsMatch(value.SwatchHex.Trim())))
                    return "A swatch needs a colour like #1a2b3c.";
            }
        }
        return null;
    }

    private static string? SpecProblem(IReadOnlyList<StoreSpecGroup>? groups)
    {
        foreach (var group in groups ?? [])
        {
            if (string.IsNullOrWhiteSpace(group.GroupName)) return "Each group of specifications needs a heading.";
            if (group.GroupName.Trim().Length > 100) return "A specification heading is 100 characters at most.";
            foreach (var item in group.Items ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Value))
                    return "Each specification needs a name and a value.";
                if (item.Name.Trim().Length > 100 || item.Value.Trim().Length > 500)
                    return "A specification's name is 100 characters at most and its value 500.";
            }
        }
        return null;
    }

    /// <summary>A SKU nobody else holds, from <paramref name="candidate"/>.</summary>
    private static async Task<string> UniqueSkuAsync(
        BenDataContext db, string candidate, CancellationToken ct, IReadOnlySet<string>? alsoTaken = null)
    {
        var sku = Truncate(Regex.Replace(candidate.ToUpperInvariant(), "[^A-Z0-9-]+", "-").Trim('-'), 58);
        if (sku.Length == 0) sku = "SKU";
        for (var n = 1; ; n++)
        {
            var attempt = n == 1 ? sku : $"{sku}-{n}";
            if ((alsoTaken is null || !alsoTaken.Contains(attempt))
                && !await db.StoreProductVariants.AnyAsync(v => v.Sku == attempt, ct))
                return attempt;
        }
    }

    internal static async Task<StoreProductAdminRecord?> LoadAsync(BenDataContext db, Guid id, CancellationToken ct)
    {
        var p = await db.StoreProducts.AsNoTracking()
            .Include(x => x.Category).ThenInclude(c => c.ParentCategory)
            .Include(x => x.Options).ThenInclude(o => o.Values)
            .Include(x => x.Variants).ThenInclude(v => v.OptionValues)
            .Include(x => x.Specs)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;

        var pictures = await db.StoreProductImages.AsNoTracking().Where(i => i.ProductId == id)
            .OrderBy(i => i.SortOrder)
            .Select(i => new StoreImageRecord(i.Id, i.UploadFileId, i.AltText, i.SortOrder, i.VariantId,
                db.UploadFileMetadata.Where(m => m.UploadFileId == i.UploadFileId).Select(m => m.WidthPixels ?? 0).FirstOrDefault(),
                db.UploadFileMetadata.Where(m => m.UploadFileId == i.UploadFileId).Select(m => m.HeightPixels ?? 0).FirstOrDefault()))
            .ToListAsync(ct);
        var ordered = await db.StoreOrderItems.AsNoTracking().Where(i => i.ProductId == id)
            .Select(i => i.VariantId).Distinct().ToListAsync(ct);
        var pendingReviews = await db.StoreReviews.CountAsync(r => r.ProductId == id && r.Status == StoreReviewStatus.Pending, ct);
        var sellerName = p.SellerAppUserId is { } seller
            ? await db.AppUsers.AsNoTracking().Where(u => u.Id == seller).Select(u => u.DisplayName ?? u.Email).FirstOrDefaultAsync(ct)
            : null;

        return new StoreProductAdminRecord(
            p.Id, p.CategoryId,
            p.Category.ParentCategory == null ? p.Category.Name : p.Category.ParentCategory.Name + " › " + p.Category.Name,
            p.Category.IsActive && (p.Category.ParentCategoryId == null || p.Category.ParentCategory!.IsActive), p.EquipmentModelId, p.Name, p.Slug,
            p.ShortDescription, p.LongDescriptionHtml, p.IsActive, p.IsFeatured, p.NewUntilUtc, p.StripeTaxCode,
            p.SortOrder, p.ViewCount, p.UnitsSold, p.AverageRating, p.ReviewCount, pendingReviews, ordered.Count > 0,
            pictures,
            p.Options.OrderBy(o => o.SortOrder).Select(o => new StoreOptionRecord(o.Id, o.Name, o.Kind,
                o.Values.OrderBy(v => v.SortOrder)
                    .Select(v => new StoreOptionValueRecord(v.Id, v.Value, v.SwatchHex, v.SortOrder, v.IsActive)).ToList())).ToList(),
            p.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Sku).Select(v => new StoreVariantAdminRecord(
                v.Id, v.Sku, StorePriceCaches.Label(v.Name), v.Price, v.CompareAtPrice, v.StockOnHand, v.StockReserved,
                v.UnitsSold, v.IsActive, v.IsDefault, v.SortOrder, v.OptionValues.Select(x => x.OptionValueId).ToList(),
                ordered.Contains(v.Id))).ToList(),
            p.Specs.OrderBy(s => s.SortOrder).GroupBy(s => s.GroupName)
                .Select(g => new StoreSpecGroup(g.Key, g.Select(s => new StoreSpecRecord(s.Name, s.Value)).ToList())).ToList(),
            $"/store/p/{p.Slug}", p.DateCreated, p.DateUpdated, p.SellerAppUserId, sellerName);
    }

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
        return -1;
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static string? TruncateOrNull(string? s, int max) => s is null ? null : Truncate(s, max);

    /// <summary>Same-type copy for the audit diff — the tracker refuses anonymous objects.</summary>
    private static StoreProduct Clone(StoreProduct p) => new()
    {
        Id = p.Id, CategoryId = p.CategoryId, EquipmentModelId = p.EquipmentModelId, Name = p.Name, Slug = p.Slug,
        ShortDescription = p.ShortDescription, LongDescriptionHtml = p.LongDescriptionHtml, IsActive = p.IsActive,
        IsFeatured = p.IsFeatured, SortOrder = p.SortOrder, NewUntilUtc = p.NewUntilUtc, StripeTaxCode = p.StripeTaxCode,
        SellerAppUserId = p.SellerAppUserId,
    };
}
