using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>Who is making an edit, and in what capacity (store sellers, backlog 251, P3).</summary>
public sealed record StoreEditActor(Guid UserId, StoreChangeActor Role)
{
    public bool IsSeller => Role == StoreChangeActor.Seller;
}

/// <summary>Why an edit was not made: the status to answer with and the sentence to show.</summary>
public sealed record StoreEditRefusal(int Status, string? Message)
{
    public static StoreEditRefusal BadRequest(string message) => new(400, message);
    public static StoreEditRefusal Conflict(string message) => new(409, message);
    public static readonly StoreEditRefusal NotFound = new(404, null);
}

/// <summary>
/// A details save. <paramref name="Admin"/> carries the fields only the store sets — a seller's
/// save leaves it null and those fields as they are.
/// </summary>
/// <param name="ExpectedDateUpdated">When the editor loaded the item; a save over somebody else's newer edit is refused.</param>
public sealed record StoreDetailsEdit(
    string? Name, Guid CategoryId, string? ShortDescription, string? LongDescriptionHtml,
    IReadOnlyList<StoreSpecGroup>? Specs, DateTime? ExpectedDateUpdated, StoreAdminDetailsEdit? Admin);

/// <summary>The details only the store sets: address, placement, tax, featuring, the seller and the equipment link.</summary>
public sealed record StoreAdminDetailsEdit(
    string? Slug, Guid? EquipmentModelId, bool IsFeatured, DateTime? NewUntilUtc, string? StripeTaxCode, int SortOrder, Guid? SellerAppUserId);

/// <summary>
/// A variant save. <paramref name="Price"/> null is a seller's: a new variant starts at $0.00 and
/// switched off, and an existing one keeps the price the store gave it.
/// </summary>
public sealed record StoreVariantEdit(
    string? Sku, decimal? Price, decimal? CompareAtPrice, bool IsActive, bool IsDefault, int SortOrder,
    IReadOnlyList<Guid>? OptionValueIds, int? InitialStock);

/// <summary>
/// The rules for changing a store item, shared by the admin's editor and the seller's (store
/// sellers, backlog 251, P3). Every change leaves its line in the item's history.
/// </summary>
/// <remarks>
/// <para><b>One place for every rule.</b> The admin and seller controllers find the item — any
/// item, or the caller's own — and hand it here; what a save may do, and the sentence it answers
/// with when it may not, is decided once. A seller's edit differs only where Ben said it does:
/// no price, no address, placement, tax, featuring or seller (09/24/2026: "not the admin part or
/// pricing").</para>
///
/// <para><b>What has been sold stays.</b> A product or variant an order names can be switched off
/// but not deleted: the order, the invoice and the refund all point at it.</para>
///
/// <para><b>Stale edits are refused.</b> A details or options save carries when the editor loaded
/// the item; if somebody saved since, the answer is a 409 in words, not a silent overwrite — the
/// seller and the store's staff can now both have the same item open.</para>
///
/// <para>Every change to variants or options recomputes the product's price range and variant
/// names (<see cref="StorePriceCaches"/>) before it answers.</para>
/// </remarks>
public sealed partial class StoreProductEditor(ICmsMarkupSanitizer sanitizer, StoreImageStorage images)
{
    public const int MaxNameLength = 200;
    public const int MaxShortDescriptionLength = 500;
    public const int MaxLongDescriptionLength = 20_000;

    public const string NotASeller = "That person isn't a seller. Give them the Seller role on their Site Roles tab first.";

    public const string StaleEdit =
        "Somebody else saved changes to this item after you opened it. Reload it to see theirs, then make yours again.";

    private const string LastLiveVariant =
        "The last live variant can't be deactivated while the product is live — take the product off sale first.";

    [GeneratedRegex("^txcd_[0-9]{8}$")]
    private static partial Regex TaxCode();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex Hex();

    /// <summary>The Seller role's holders, as a query.</summary>
    public static IQueryable<Guid> SellerIds(BenDataContext db)
        => db.UserRoles.Where(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Ben.Data.Common.Constants.RoleNames.Seller)).Select(ur => ur.UserId);

    /// <summary>A save is stale when the item has been saved since the editor loaded it.</summary>
    private static bool Stale(StoreProduct product, DateTime? expected)
        => expected is { } when && (product.DateUpdated ?? product.DateCreated) != when;

    // ── making ───────────────────────────────────────────────────────────────

    /// <summary>A new item from just a name: hidden, with one $0.00 variant to price. A seller's is theirs.</summary>
    public async Task<(StoreProduct? Product, StoreEditRefusal? Refusal)> CreateAsync(
        BenDataContext db, string? rawName, Guid? categoryId, StoreEditActor actor, CancellationToken ct)
    {
        var name = rawName?.Trim();
        if (string.IsNullOrEmpty(name)) return (null, StoreEditRefusal.BadRequest("A product needs a name."));
        if (name.Length > MaxNameLength) return (null, StoreEditRefusal.BadRequest($"A product name is {MaxNameLength} characters at most."));

        var category = categoryId
            ?? await db.StoreCategories.OrderBy(c => c.SortOrder).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (category is null || !await db.StoreCategories.AnyAsync(c => c.Id == category, ct))
            return (null, StoreEditRefusal.BadRequest("Add a category first — every product is filed under one."));

        var now = DateTime.UtcNow;
        var product = new StoreProduct
        {
            Id = Guid.NewGuid(), CategoryId = category.Value, Name = name, IsActive = false,
            SortOrder = (await db.StoreProducts.Where(p => p.CategoryId == category)
                .MaxAsync(p => (int?)p.SortOrder, ct) ?? -1) + 1,
            SellerAppUserId = actor.IsSeller ? actor.UserId : null,
            DateCreated = now, CreatedByAppUserId = actor.UserId,
        };
        product.Slug = await StoreSlugs.ForProductAsync(db, product.Id, name, ct);
        db.StoreProducts.Add(product);
        db.StoreProductVariants.Add(new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = product.Id, Sku = await UniqueSkuAsync(db, product.Slug, ct),
            OptionSignature = string.Empty, Price = 0m, IsActive = true, IsDefault = true,
            DateCreated = now, CreatedByAppUserId = actor.UserId,
        });
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Created, "Created it.", actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        return (product, null);
    }

    // ── details ──────────────────────────────────────────────────────────────

    /// <summary>Names, words, specifications and — for the store — the fields only it sets.</summary>
    public async Task<StoreEditRefusal?> SaveDetailsAsync(
        BenDataContext db, StoreProduct product, StoreDetailsEdit edit, StoreEditActor actor, CancellationToken ct)
    {
        await db.Entry(product).Collection(p => p.Specs).LoadAsync(ct);
        var was = StoreProductHistory.DetailsSnapshot.Of(product, product.Specs);
        if (Stale(product, edit.ExpectedDateUpdated)) return StoreEditRefusal.Conflict(StaleEdit);

        var name = edit.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return StoreEditRefusal.BadRequest("A product needs a name.");
        if (name.Length > MaxNameLength) return StoreEditRefusal.BadRequest($"A product name is {MaxNameLength} characters at most.");
        if (!await db.StoreCategories.AnyAsync(c => c.Id == edit.CategoryId, ct))
            return StoreEditRefusal.BadRequest("That category no longer exists.");
        var shortDescription = Trimmed(edit.ShortDescription);
        if (shortDescription?.Length > MaxShortDescriptionLength)
            return StoreEditRefusal.BadRequest($"The short description is {MaxShortDescriptionLength} characters at most.");
        if (SpecProblem(edit.Specs) is { } badSpec) return StoreEditRefusal.BadRequest(badSpec);

        string? taxCode = null;
        if (edit.Admin is { } admin)
        {
            if (admin.EquipmentModelId is { } modelId && !await db.EquipmentModels.AnyAsync(m => m.Id == modelId, ct))
                return StoreEditRefusal.BadRequest("That equipment model no longer exists.");
            taxCode = Trimmed(admin.StripeTaxCode);
            if (taxCode is not null && !TaxCode().IsMatch(taxCode))
                return StoreEditRefusal.BadRequest("A Stripe tax code looks like txcd_99999999.");
            // A seller is named only from the Seller role; one who has since lost it can stay on the
            // item they already had, so an unrelated save does not fail over it.
            if (admin.SellerAppUserId is { } sellerId && sellerId != product.SellerAppUserId
                && !await SellerIds(db).AnyAsync(u => u == sellerId, ct))
                return StoreEditRefusal.BadRequest(NotASeller);

            if (!string.IsNullOrWhiteSpace(admin.Slug))
            {
                var slug = StoreSlugs.Typed(admin.Slug);
                if (slug is null) return StoreEditRefusal.BadRequest("A web address needs letters or digits in it.");
                if (await db.StoreProducts.AnyAsync(p => p.Id != product.Id && p.Slug == slug, ct))
                    return StoreEditRefusal.Conflict($"There is already a product at /store/p/{slug}.");
                product.Slug = slug;
            }
        }

        var html = sanitizer.SanitizeHtml(edit.LongDescriptionHtml);
        if (html.Length > MaxLongDescriptionLength)
            return StoreEditRefusal.BadRequest($"The description is {MaxLongDescriptionLength:N0} characters at most.");

        product.CategoryId = edit.CategoryId;
        product.Name = name;
        product.ShortDescription = shortDescription;
        product.LongDescriptionHtml = string.IsNullOrWhiteSpace(html) ? null : html;
        if (edit.Admin is { } fields)
        {
            product.EquipmentModelId = fields.EquipmentModelId;
            product.IsFeatured = fields.IsFeatured;
            product.NewUntilUtc = fields.NewUntilUtc;
            product.StripeTaxCode = taxCode;
            product.SortOrder = fields.SortOrder;
            product.SellerAppUserId = fields.SellerAppUserId;
        }
        var now = DateTime.UtcNow;
        product.DateUpdated = now;
        product.UpdatedByAppUserId = actor.UserId;

        db.StoreProductSpecs.RemoveRange(product.Specs);
        var specs = new List<StoreProductSpec>();
        var order = 0;
        foreach (var group in edit.Specs ?? [])
            foreach (var item in group.Items ?? [])
                specs.Add(new StoreProductSpec
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, GroupName = group.GroupName.Trim(),
                    Name = item.Name.Trim(), Value = item.Value.Trim(), SortOrder = order++,
                    DateCreated = now, CreatedByAppUserId = actor.UserId,
                });
        db.StoreProductSpecs.AddRange(specs);

        var becomes = StoreProductHistory.DetailsSnapshot.Of(product, specs);
        StoreProductHistory.Record(db, product.Id,
            StoreProductHistory.DescribeDetails(was, becomes, await HistoryNamesAsync(db, was, becomes, ct)),
            actor.UserId, actor.Role, now);

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>The names a details line needs: the categories, equipment and sellers that changed.</summary>
    private static async Task<Dictionary<Guid, string>> HistoryNamesAsync(
        BenDataContext db, StoreProductHistory.DetailsSnapshot was, StoreProductHistory.DetailsSnapshot now, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        if (was.CategoryId != now.CategoryId)
            foreach (var c in await db.StoreCategories.AsNoTracking().Where(c => c.Id == was.CategoryId || c.Id == now.CategoryId)
                         .Select(c => new { c.Id, c.Name }).ToListAsync(ct))
                names[c.Id] = c.Name;
        if (was.EquipmentModelId != now.EquipmentModelId && now.EquipmentModelId is { } model)
            foreach (var m in await db.EquipmentModels.AsNoTracking().Where(m => m.Id == model)
                         .Select(m => new { m.Id, m.Name, Brand = m.EquipmentBrand.Name }).ToListAsync(ct))
                names[m.Id] = $"{m.Brand} {m.Name}";
        if (was.SellerAppUserId != now.SellerAppUserId)
            foreach (var u in await db.AppUsers.AsNoTracking().Where(u => u.Id == was.SellerAppUserId || u.Id == now.SellerAppUserId)
                         .Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct))
                names[u.Id] = u.DisplayName ?? "a seller";   // never an email: the seller reads this too
        return names;
    }

    // ── on sale and off ──────────────────────────────────────────────────────

    /// <summary>
    /// Puts an item on sale or takes it off. Going on sale is checked (<see cref="StoreProductSale"/>);
    /// the first time an item goes on sale is kept, so "a draft that never sold" stays answerable.
    /// </summary>
    public async Task<StoreEditRefusal?> SwitchAsync(
        BenDataContext db, StoreProduct product, bool on, StoreEditActor actor, CancellationToken ct)
    {
        if (on && await StoreProductSale.FirstProblemAsync(db, product.Id, ct) is { } problem)
            return StoreEditRefusal.BadRequest(problem);
        if (product.IsActive == on) return null;

        var now = DateTime.UtcNow;
        product.IsActive = on;
        if (on) product.FirstOnSaleUtc ??= now;
        // Back on sale by hand: no longer "discontinued" or selling out (store sellers P13).
        if (on) (product.DiscontinuedUtc, product.SellingOutSinceUtc) = (null, null);
        product.DateUpdated = now;
        product.UpdatedByAppUserId = actor.UserId;
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Sale, on ? "Put it on sale." : "Took it off sale.",
            actor.UserId, actor.Role, now);
        // A new version's first day on sale: what the seller chose for the one it replaces happens now.
        var replaced = on ? await StoreProductVersions.ApplyPolicyAsync(db, product, actor, now, ct) : null;
        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, product.Id, ct);
        if (replaced is { } previous) await StorePriceCaches.RecomputeAsync(db, previous, ct);
        return null;
    }

    /// <summary>
    /// Removes an item nobody has bought. A seller's may go only while it is a draft that has never
    /// been on sale; one that has is taken off sale instead.
    /// </summary>
    public async Task<StoreEditRefusal?> DeleteAsync(BenDataContext db, StoreProduct product, StoreEditActor actor, CancellationToken ct)
    {
        if (await db.StoreOrderItems.AnyAsync(i => i.ProductId == product.Id, ct))
            return StoreEditRefusal.BadRequest("That product has been sold, so its record must stay. Deactivate it instead.");
        if (actor.IsSeller && (product.IsActive || product.FirstOnSaleUtc is not null))
            return StoreEditRefusal.BadRequest("Only a draft that has never been on sale can be deleted. Take it off sale instead.");
        if (await db.StoreProducts.AnyAsync(p => p.PreviousVersionProductId == product.Id, ct))
            return StoreEditRefusal.BadRequest("A newer version links back to this one, so it must stay. Take it off sale instead.");

        var id = product.Id;
        var pictures = await db.StoreProductImages.Where(i => i.ProductId == id).Select(i => i.UploadFileId).ToListAsync(ct);
        pictures.AddRange(await db.StoreProductParts.Where(x => x.ProductId == id && x.ThumbnailUploadFileId != null)
            .Select(x => x.ThumbnailUploadFileId!.Value).ToListAsync(ct));
        pictures.AddRange(await db.StoreProductFiles.Where(x => x.ProductId == id && x.UploadFileId != null)
            .Select(x => x.UploadFileId!.Value).ToListAsync(ct));

        // Children first on the NoAction paths (a variant's choices point at values that also
        // cascade from the product; a picture's variant likewise), then the product takes the rest.
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await db.StoreProductVariantOptionValues.Where(x => x.Variant.ProductId == id).ExecuteDeleteAsync(ct);
            await db.StoreProductImages.Where(i => i.ProductId == id).ExecuteDeleteAsync(ct);
            await db.StoreProductSaleRequests.Where(r => r.ProductId == id).ExecuteDeleteAsync(ct);
            await db.StoreProductFiles.Where(f => f.ProductId == id).ExecuteDeleteAsync(ct);
            await db.StoreProducts.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }

        foreach (var file in pictures) await images.RemoveAsync(db, file, ct);
        return null;
    }

    // ── options and variants ─────────────────────────────────────────────────

    /// <summary>Replaces an item's options with this list. A value a variant still uses cannot go.</summary>
    public async Task<StoreEditRefusal?> SaveOptionsAsync(
        BenDataContext db, Guid productId, IReadOnlyList<SaveStoreOptionRequest>? options, DateTime? expectedDateUpdated,
        StoreEditActor actor, CancellationToken ct)
    {
        var product = await db.StoreProducts.Include(p => p.Options).ThenInclude(o => o.Values)
            .FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return StoreEditRefusal.NotFound;
        if (Stale(product, expectedDateUpdated)) return StoreEditRefusal.Conflict(StaleEdit);

        var wanted = options ?? [];
        if (wanted.Count > StoreCatalogRules.MaxOptionsPerProduct)
            return StoreEditRefusal.BadRequest("Three options is the most a product can carry.");
        if (OptionsProblem(wanted, product) is { } problem) return StoreEditRefusal.BadRequest(problem);

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
                return StoreEditRefusal.BadRequest($"{leaving.First(v => v.Id == used.OptionValueId).Value} is still used by variant "
                                                 + $"{StorePriceCaches.Label(used.Name)} — remove it from the variant first.");
        }

        var now = DateTime.UtcNow;
        var optionsWere = OptionsKey(product.Options.OrderBy(o => o.SortOrder)
            .Select(o => (o.Name, o.Values.OrderBy(v => v.SortOrder).Select(v => (v.Value, v.IsActive, v.SwatchHex)))));
        var keptOptions = wanted.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
        db.StoreProductOptionValues.RemoveRange(leaving);
        db.StoreProductOptions.RemoveRange(product.Options.Where(o => !keptOptions.Contains(o.Id)));

        for (var i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            var option = w.Id is { } oid ? product.Options.Single(o => o.Id == oid) : null;
            if (option is null)
            {
                option = new StoreProductOption { Id = Guid.NewGuid(), ProductId = productId, DateCreated = now, CreatedByAppUserId = actor.UserId };
                db.StoreProductOptions.Add(option);
            }
            else { option.DateUpdated = now; option.UpdatedByAppUserId = actor.UserId; }
            option.Name = w.Name.Trim();
            option.Kind = w.Kind;
            option.SortOrder = i;

            for (var j = 0; j < w.Values.Count; j++)
            {
                var wv = w.Values[j];
                var value = wv.Id is { } vid ? option.Values.Single(v => v.Id == vid) : null;
                if (value is null)
                {
                    value = new StoreProductOptionValue { Id = Guid.NewGuid(), OptionId = option.Id, DateCreated = now, CreatedByAppUserId = actor.UserId };
                    db.StoreProductOptionValues.Add(value);
                }
                else { value.DateUpdated = now; value.UpdatedByAppUserId = actor.UserId; }
                value.Value = wv.Value.Trim();
                value.SwatchHex = w.Kind == StoreOptionKind.Swatch ? wv.SwatchHex!.Trim().ToLowerInvariant() : null;
                value.IsActive = wv.IsActive;
                value.SortOrder = j;
            }
        }

        product.DateUpdated = now;
        product.UpdatedByAppUserId = actor.UserId;
        var optionsNow = OptionsKey(wanted.Select(o => (o.Name.Trim(),
            o.Values.Select(v => (v.Value.Trim(), v.IsActive, o.Kind == StoreOptionKind.Swatch ? v.SwatchHex!.Trim().ToLowerInvariant() : null)))));
        if (optionsNow.Said != optionsWere.Said)
            StoreProductHistory.Record(db, productId, StoreProductChangeArea.Options, optionsNow.Said, actor.UserId, actor.Role, now);
        else if (optionsNow.Colours != optionsWere.Colours)
            StoreProductHistory.Record(db, productId, StoreProductChangeArea.Options, "Changed the swatch colours.", actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, productId, ct);
        return null;
    }

    /// <summary>What an option list says in the history, and its swatch colours apart.</summary>
    private static (string Said, string Colours) OptionsKey(
        IEnumerable<(string Name, IEnumerable<(string Value, bool IsActive, string? SwatchHex)> Values)> options)
    {
        var list = options.Select(o => (o.Name, Values: o.Values.ToList())).ToList();
        return (StoreProductHistory.DescribeOptions(list.Select(o => (o.Name, o.Values.Select(v => v.IsActive ? v.Value : $"{v.Value} (off)")))),
                string.Join("|", list.SelectMany(o => o.Values.Select(v => v.SwatchHex))));
    }

    /// <summary>
    /// Adds a variant for every combination of active option values the item lacks. The option-less
    /// default goes once real combinations exist — unless it has been sold or holds stock. The
    /// store's new variants copy the price of the one they grew from; a seller's start off and
    /// unpriced, and a seller cannot do this to an item on sale, which would leave it nothing to sell.
    /// </summary>
    public async Task<(int Added, StoreEditRefusal? Refusal)> GenerateVariantsAsync(
        BenDataContext db, Guid productId, StoreEditActor actor, CancellationToken ct)
    {
        var product = await db.StoreProducts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null) return (0, StoreEditRefusal.NotFound);
        if (actor.IsSeller && product.IsActive)
            return (0, StoreEditRefusal.BadRequest(
                "Take it off sale before making variants from its choices — the store prices each new one before it can sell."));
        var options = await db.StoreProductOptions.AsNoTracking().Where(o => o.ProductId == productId)
            .OrderBy(o => o.SortOrder)
            .Select(o => o.Values.Where(v => v.IsActive).OrderBy(v => v.SortOrder).Select(v => new { v.Id, v.Value }).ToList())
            .ToListAsync(ct);
        if (options.Count == 0 || options.Any(o => o.Count == 0))
            return (0, StoreEditRefusal.BadRequest("Give every option at least one value first."));

        var variants = await db.StoreProductVariants.Where(v => v.ProductId == productId).ToListAsync(ct);
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
                Id = Guid.NewGuid(), ProductId = productId, Sku = sku, OptionSignature = signature,
                Price = actor.IsSeller ? 0m : template?.Price ?? 0m, IsActive = !actor.IsSeller, SortOrder = order++,
                DateCreated = now, CreatedByAppUserId = actor.UserId,
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
            // The first live variant shows first; a seller's are all off until priced, so then
            // simply the first of them.
            if (!variants.Any(v => v.IsDefault && v.IsActive))
            {
                if (variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).FirstOrDefault() is { } live) live.IsDefault = true;
                else if (!variants.Any(v => v.IsDefault)) variants.Where(v => v != bare).OrderBy(v => v.SortOrder).First().IsDefault = true;
            }
        }

        if (added > 0)
            StoreProductHistory.Record(db, productId, StoreProductChangeArea.Variants,
                (added == 1 ? "Added a variant for the one new combination of choices." : $"Added {added} variants, one for each new combination of choices.")
                + (actor.IsSeller ? " They're off until the store prices them." : ""),
                actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, productId, ct);
        return (added, null);
    }

    public async Task<StoreEditRefusal?> CreateVariantAsync(
        BenDataContext db, StoreProduct product, StoreVariantEdit edit, StoreEditActor actor, CancellationToken ct)
    {
        if (edit.InitialStock is < 0) return StoreEditRefusal.BadRequest("Starting stock can't be negative.");

        var variant = new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = product.Id, DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor.UserId,
        };
        // A seller's new variant waits, off and unpriced, for the store to price it.
        if (actor.IsSeller) edit = edit with { Price = 0m, CompareAtPrice = null, IsActive = false };
        if (await ApplyVariantAsync(db, product, variant, edit, isNew: true, ct) is { } refused) return refused;
        db.StoreProductVariants.Add(variant);
        StoreProductHistory.Record(db, product.Id, StoreProductHistory.DescribeNewVariant(variant, edit.InitialStock ?? 0),
            actor.UserId, actor.Role, variant.DateCreated);
        await db.SaveChangesAsync(ct);

        if (edit.InitialStock is > 0 and var stock)
            await StoreStock.AdjustAsync(db, variant.Id, stock, StoreStockReason.Received, "Starting stock", actor.UserId, DateTime.UtcNow, ct);

        await StorePriceCaches.RecomputeAsync(db, product.Id, ct);
        return null;
    }

    public async Task<StoreEditRefusal?> UpdateVariantAsync(
        BenDataContext db, StoreProduct product, Guid variantId, StoreVariantEdit edit, StoreEditActor actor, CancellationToken ct)
    {
        var variant = await db.StoreProductVariants.Include(v => v.OptionValues)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == product.Id, ct);
        if (variant is null) return StoreEditRefusal.NotFound;

        if (product.IsActive && variant.IsActive && !edit.IsActive
            && !await db.StoreProductVariants.AnyAsync(v => v.ProductId == product.Id && v.Id != variantId && v.IsActive, ct))
            return StoreEditRefusal.BadRequest(LastLiveVariant);

        // A seller's save keeps the store's prices, whatever it carries.
        if (actor.IsSeller)
        {
            edit = edit with { Price = variant.Price, CompareAtPrice = variant.CompareAtPrice };
            if (edit.IsActive && !variant.IsActive && variant.Price <= 0m)
                return StoreEditRefusal.BadRequest($"The store prices {StorePriceCaches.Label(variant.Name)} before it can be switched on.");
        }

        var was = StoreProductHistory.VariantSnapshot.Of(variant);
        if (await ApplyVariantAsync(db, product, variant, edit, isNew: false, ct) is { } refused) return refused;
        variant.DateUpdated = DateTime.UtcNow;
        variant.UpdatedByAppUserId = actor.UserId;
        StoreProductHistory.Record(db, product.Id, StoreProductHistory.DescribeVariant(was, StoreProductHistory.VariantSnapshot.Of(variant)),
            actor.UserId, actor.Role, variant.DateUpdated.Value);
        await db.SaveChangesAsync(ct);

        await StorePriceCaches.RecomputeAsync(db, product.Id, ct);
        return null;
    }

    public async Task<StoreEditRefusal?> DeleteVariantAsync(
        BenDataContext db, StoreProduct product, Guid variantId, StoreEditActor actor, CancellationToken ct)
    {
        var variant = await db.StoreProductVariants.FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == product.Id, ct);
        if (variant is null) return StoreEditRefusal.NotFound;

        if (await db.StoreOrderItems.AnyAsync(i => i.VariantId == variantId, ct))
            return StoreEditRefusal.BadRequest("That variant has been ordered — deactivate it instead.");
        var others = await db.StoreProductVariants.Where(v => v.ProductId == product.Id && v.Id != variantId).ToListAsync(ct);
        if (others.Count == 0) return StoreEditRefusal.BadRequest("A product keeps at least one variant.");
        if (product.IsActive && variant.IsActive && !others.Any(v => v.IsActive)) return StoreEditRefusal.BadRequest(LastLiveVariant);

        await db.StoreProductImages.Where(i => i.VariantId == variantId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.VariantId, (Guid?)null), ct);
        db.StoreProductVariants.Remove(variant);
        if (variant.IsDefault) (others.FirstOrDefault(v => v.IsActive) ?? others[0]).IsDefault = true;
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Variants,
            $"Removed {StorePriceCaches.Label(variant.Name)} ({variant.Sku}).", actor.UserId, actor.Role, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);

        await StorePriceCaches.RecomputeAsync(db, product.Id, ct);
        return null;
    }

    /// <summary>Copies a variant edit onto the row, or answers why not.</summary>
    private static async Task<StoreEditRefusal?> ApplyVariantAsync(
        BenDataContext db, StoreProduct product, StoreProductVariant variant, StoreVariantEdit edit, bool isNew, CancellationToken ct)
    {
        var price = edit.Price ?? 0m;
        var sku = edit.Sku?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(sku)) return StoreEditRefusal.BadRequest("A variant needs a SKU.");
        if (sku.Length > 64) return StoreEditRefusal.BadRequest("A SKU is 64 characters at most.");
        if (price < 0m) return StoreEditRefusal.BadRequest("A price can't be negative.");
        if (price != StoreMoney.Round(price) || edit.CompareAtPrice is { } c && c != StoreMoney.Round(c))
            return StoreEditRefusal.BadRequest("Prices are dollars and cents — two decimal places at most.");
        if (edit.CompareAtPrice is { } old && old <= price)
            return StoreEditRefusal.BadRequest("The old price has to be higher than the price.");
        if (product.IsActive && edit.IsActive && price <= 0m)
            return StoreEditRefusal.BadRequest("This product is on sale, so the variant needs a price above $0.00.");

        var clash = await db.StoreProductVariants.AsNoTracking()
            .Where(v => v.Id != variant.Id && v.Sku == sku)
            .Select(v => new { Product = v.Product.Name, v.Name })
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
            return StoreEditRefusal.Conflict($"SKU {sku} is already used by {clash.Product} — {StorePriceCaches.Label(clash.Name)}.");

        // One value for each of the product's options, and only its own.
        var options = await db.StoreProductOptions.AsNoTracking().Where(o => o.ProductId == product.Id)
            .OrderBy(o => o.SortOrder)
            .Select(o => new { o.Id, o.Name, Values = o.Values.Select(v => new { v.Id, v.Value }).ToList() })
            .ToListAsync(ct);
        var chosen = (edit.OptionValueIds ?? []).Distinct().ToList();
        var byOption = options.Select(o => (o.Name, Picked: o.Values.Where(v => chosen.Contains(v.Id)).ToList())).ToList();
        var missing = byOption.Where(o => o.Picked.Count != 1).Select(o => o.Name).ToList();
        if (missing.Count > 0) return StoreEditRefusal.BadRequest($"Pick one value for each option: {string.Join(", ", missing)}.");
        if (chosen.Count != byOption.Sum(o => o.Picked.Count))
            return StoreEditRefusal.BadRequest("One of those choices belongs to another product.");

        var signature = StorePriceCaches.Signature(chosen);
        if (await db.StoreProductVariants.AnyAsync(v => v.ProductId == product.Id && v.Id != variant.Id && v.OptionSignature == signature, ct))
        {
            var label = byOption.Count == 0 ? StorePriceCaches.DefaultLabel : string.Join(" / ", byOption.Select(o => o.Picked[0].Value));
            return StoreEditRefusal.BadRequest($"A variant with {label} already exists.");
        }

        variant.Sku = sku;
        variant.Price = price;
        variant.CompareAtPrice = edit.CompareAtPrice;
        variant.IsActive = edit.IsActive;
        variant.SortOrder = edit.SortOrder;
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

        if (edit.IsDefault && !variant.IsDefault)
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
    public static async Task<string> UniqueSkuAsync(
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

    // ── stock ────────────────────────────────────────────────────────────────

    /// <summary>Why a stock request cannot be applied as asked, or null. Shared with the stock page.</summary>
    public static string? StockRequestProblem(int? delta, int? setTo, StoreStockReason reason)
    {
        if (delta is not null && setTo is not null) return "Give either a change or a new quantity, not both.";
        if (delta is null or 0 && setTo is null) return "Give a change or a new quantity.";
        if (setTo is < 0) return "A count can't be below zero.";
        return reason is StoreStockReason.Received or StoreStockReason.Correction or StoreStockReason.Damaged
            ? null
            : $"'{reason}' is written by the system, not by hand.";
    }

    /// <summary>A change or a counted quantity for one variant; it leaves a movement row and a history line.</summary>
    public static async Task<StoreEditRefusal?> AdjustStockAsync(
        BenDataContext db, Guid productId, Guid variantId, AdjustStockRequest request, StoreEditActor actor, CancellationToken ct)
    {
        if (StockRequestProblem(request.Delta, request.SetTo, request.Reason) is { } problem) return StoreEditRefusal.BadRequest(problem);
        if (!await db.StoreProductVariants.AnyAsync(v => v.Id == variantId && v.ProductId == productId, ct)) return StoreEditRefusal.NotFound;

        var refusal = await StoreProductHistory.AdjustStockAsync(db, [new StoreStockChange(variantId, request.Delta, request.SetTo)],
            request.Reason, request.Note, actor.UserId, actor.Role, DateTime.UtcNow, ct);
        return refusal is null ? null : StoreEditRefusal.BadRequest(refusal);
    }

    // ── pictures ─────────────────────────────────────────────────────────────

    public async Task<StoreEditRefusal?> AddImageAsync(
        BenDataContext db, Guid productId, byte[] bytes, string? contentType, string? fileName, string? altText, Guid? variantId,
        StoreEditActor actor, CancellationToken ct)
    {
        if (bytes.Length == 0) return StoreEditRefusal.BadRequest("There was no picture in that upload.");
        if (await db.StoreProductImages.CountAsync(i => i.ProductId == productId, ct) >= StoreCatalogRules.MaxImagesPerProduct)
            return StoreEditRefusal.BadRequest($"This product already has {StoreCatalogRules.MaxImagesPerProduct} pictures.");
        if (variantId is { } v && !await db.StoreProductVariants.AnyAsync(x => x.Id == v && x.ProductId == productId, ct))
            return StoreEditRefusal.BadRequest("That variant belongs to another product.");

        var (stored, refusal) = await images.SaveAsync(db, bytes, contentType, fileName, $"products/{productId:N}", actor.UserId, ct);
        if (refusal == StoreImageRefusal.NotAPicture) return StoreEditRefusal.BadRequest("A product picture is a photograph — JPEG, PNG or similar.");
        if (stored is null) return StoreEditRefusal.BadRequest("That picture could not be read.");

        db.StoreProductImages.Add(new StoreProductImage
        {
            Id = Guid.NewGuid(), ProductId = productId, UploadFileId = stored.Id, VariantId = variantId,
            AltText = TruncateOrNull(Trimmed(altText), 200),
            SortOrder = (await db.StoreProductImages.Where(i => i.ProductId == productId).MaxAsync(i => (int?)i.SortOrder, ct) ?? -1) + 1,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actor.UserId,
        });
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Pictures,
            variantId is { } shows ? $"Added a picture of {await VariantLabelAsync(db, shows, ct)}." : "Added a picture.",
            actor.UserId, actor.Role, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public static async Task<StoreEditRefusal?> ReorderImagesAsync(
        BenDataContext db, Guid productId, IReadOnlyList<Guid>? orderedIds, StoreEditActor actor, CancellationToken ct)
    {
        var pictures = await db.StoreProductImages.Where(i => i.ProductId == productId).ToListAsync(ct);
        var ordered = orderedIds ?? [];
        if (ordered.Count != pictures.Count || !pictures.All(p => ordered.Contains(p.Id)))
            return StoreEditRefusal.BadRequest("That isn't the full list of pictures.");

        var moved = pictures.Any(p => p.SortOrder != IndexOf(ordered, p.Id));
        foreach (var picture in pictures) picture.SortOrder = IndexOf(ordered, picture.Id);
        if (moved) StoreProductHistory.Record(db, productId, StoreProductChangeArea.Pictures, "Reordered the pictures.", actor.UserId, actor.Role, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public static async Task<StoreEditRefusal?> UpdateImageAsync(
        BenDataContext db, Guid productId, Guid imageId, UpdateStoreImageRequest request, StoreEditActor actor, CancellationToken ct)
    {
        var picture = await db.StoreProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId, ct);
        if (picture is null) return StoreEditRefusal.NotFound;
        if (request.VariantId is { } v && !await db.StoreProductVariants.AnyAsync(x => x.Id == v && x.ProductId == productId, ct))
            return StoreEditRefusal.BadRequest("That variant belongs to another product.");
        var alt = Trimmed(request.AltText);
        if (alt?.Length > 200) return StoreEditRefusal.BadRequest("A picture's description is 200 characters at most.");

        var said = new List<string>();
        if (picture.AltText != alt) said.Add("Changed a picture’s description.");
        if (picture.VariantId != request.VariantId)
            said.Add(request.VariantId is { } shows ? $"Showed a picture with {await VariantLabelAsync(db, shows, ct)}." : "Showed a picture with every variant.");
        picture.AltText = alt;
        picture.VariantId = request.VariantId;
        picture.DateUpdated = DateTime.UtcNow;
        picture.UpdatedByAppUserId = actor.UserId;
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Pictures, string.Join(" ", said), actor.UserId, actor.Role, picture.DateUpdated.Value);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<StoreEditRefusal?> DeleteImageAsync(
        BenDataContext db, StoreProduct product, Guid imageId, StoreEditActor actor, CancellationToken ct)
    {
        var picture = await db.StoreProductImages.FirstOrDefaultAsync(i => i.Id == imageId && i.ProductId == product.Id, ct);
        if (picture is null) return StoreEditRefusal.NotFound;
        if (product.IsActive && !await db.StoreProductImages.AnyAsync(i => i.ProductId == product.Id && i.Id != imageId, ct))
            return StoreEditRefusal.BadRequest("A live product keeps at least one picture. Add another first, or take it off sale.");

        db.StoreProductImages.Remove(picture);
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Pictures, "Removed a picture.", actor.UserId, actor.Role, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        await images.RemoveAsync(db, picture.UploadFileId, ct);
        return null;
    }

    // ── parts and cost (P4) ──────────────────────────────────────────────────

    public const int MaxParts = 100;

    /// <summary>
    /// Replaces an item's parts list and its "other" cost line. Parts missing from the list go, with
    /// their pictures. The line it leaves says what a unit now costs to make.
    /// </summary>
    public async Task<StoreEditRefusal?> SavePartsAsync(
        BenDataContext db, StoreProduct product, SaveStorePartsRequest request, StoreEditActor actor, CancellationToken ct)
    {
        if (Stale(product, request.ExpectedDateUpdated)) return StoreEditRefusal.Conflict(StaleEdit);
        var wanted = request.Parts ?? [];
        if (wanted.Count > MaxParts) return StoreEditRefusal.BadRequest($"A parts list is {MaxParts} parts at most.");
        if (PartsProblem(wanted, request.OtherCostPerUnit, request.OtherCostNote) is { } problem) return StoreEditRefusal.BadRequest(problem);

        var parts = await db.StoreProductParts.Where(x => x.ProductId == product.Id).ToListAsync(ct);
        if (wanted.FirstOrDefault(w => w.Id is { } id && parts.All(x => x.Id != id)) is not null)
            return StoreEditRefusal.BadRequest("One of those parts belongs to another item.");

        var costWas = CostOf(parts, product.OtherCostPerUnit);
        var listWas = PartsKey(parts.OrderBy(x => x.SortOrder).Select(x => (x.Name, x.PriceBasis, x.Price, x.PiecesPerPack, x.QuantityPerUnit, x.InfoUrl, x.BuyUrl)));
        var countsWere = string.Join("|", parts.OrderBy(x => x.SortOrder).Select(x => x.OnHand));
        var noteWas = product.OtherCostNote;

        var now = DateTime.UtcNow;
        var keep = wanted.Where(w => w.Id is not null).Select(w => w.Id!.Value).ToHashSet();
        var leaving = parts.Where(x => !keep.Contains(x.Id)).ToList();
        db.StoreProductParts.RemoveRange(leaving);
        for (var i = 0; i < wanted.Count; i++)
        {
            var w = wanted[i];
            var part = w.Id is { } id ? parts.Single(x => x.Id == id) : null;
            if (part is null)
            {
                part = new StoreProductPart { Id = Guid.NewGuid(), ProductId = product.Id, DateCreated = now };
                db.StoreProductParts.Add(part);
            }
            else part.DateUpdated = now;
            part.Name = w.Name.Trim();
            part.PriceBasis = w.PriceBasis;
            part.Price = w.Price;
            part.PiecesPerPack = w.PriceBasis == StorePartPriceBasis.PerPiece ? 1 : w.PiecesPerPack;
            part.QuantityPerUnit = w.QuantityPerUnit;
            part.InfoUrl = Trimmed(w.InfoUrl);
            part.BuyUrl = Trimmed(w.BuyUrl);
            part.OnHand = w.OnHand;
            part.SortOrder = i;
        }
        product.OtherCostPerUnit = request.OtherCostPerUnit;
        product.OtherCostNote = Trimmed(request.OtherCostNote);
        product.DateUpdated = now;
        product.UpdatedByAppUserId = actor.UserId;

        var kept = parts.Where(x => keep.Contains(x.Id)).Concat(db.StoreProductParts.Local.Where(x => x.ProductId == product.Id && parts.All(p => p.Id != x.Id))).ToList();
        var costNow = CostOf(kept, product.OtherCostPerUnit);
        var listNow = PartsKey(wanted.Select(w => (w.Name.Trim(), w.PriceBasis, w.Price, w.PriceBasis == StorePartPriceBasis.PerPiece ? 1 : w.PiecesPerPack,
            w.QuantityPerUnit, Trimmed(w.InfoUrl), Trimmed(w.BuyUrl))));
        var countsNow = string.Join("|", wanted.Select(w => w.OnHand));

        string? said = null;
        if (costNow != costWas)
            said = $"Changed the parts list — a unit now costs {StoreMoney.Format(costNow)} to make (was {StoreMoney.Format(costWas)}).";
        else if (listNow != listWas || noteWas != product.OtherCostNote)
            said = $"Changed the parts list; a unit still costs {StoreMoney.Format(costNow)} to make.";
        else if (countsNow != countsWere)
            said = "Counted the parts on hand.";
        if (said is not null) StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Parts, said, actor.UserId, actor.Role, now);

        await db.SaveChangesAsync(ct);
        foreach (var file in leaving.Where(x => x.ThumbnailUploadFileId is not null)) await images.RemoveAsync(db, file.ThumbnailUploadFileId!.Value, ct);
        return null;
    }

    private static decimal CostOf(IEnumerable<StoreProductPart> parts, decimal other)
        => StoreCostMath.CostBasis(parts.Select(x => StoreCostMath.PartCostPerUnit(x.PriceBasis, x.Price, x.PiecesPerPack, x.QuantityPerUnit)), other);

    private static string PartsKey(IEnumerable<(string, StorePartPriceBasis, decimal, int, decimal, string?, string?)> parts)
        => string.Join("\n", parts.Select(x => x.ToString()));

    private static string? PartsProblem(IReadOnlyList<SaveStorePartRequest> parts, decimal other, string? note)
    {
        static bool FourPlaces(decimal d) => d == Math.Round(d, 4);
        foreach (var part in parts)
        {
            var name = part.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return "Each part needs a name.";
            if (name.Length > StoreProductPart.MaxNameLength) return $"A part's name is {StoreProductPart.MaxNameLength} characters at most.";
            if (part.Price < 0m) return $"{name}: a price can't be negative.";
            if (!FourPlaces(part.Price)) return $"{name}: a price goes to the hundredth of a cent at most — 0.0699.";
            if (part.PriceBasis == StorePartPriceBasis.PerPack && part.PiecesPerPack < 1) return $"{name}: a pack holds at least one piece.";
            if (part.QuantityPerUnit < 0m) return $"{name}: the number used in a unit can't be negative.";
            if (!FourPlaces(part.QuantityPerUnit)) return $"{name}: the number used in a unit goes to four decimal places at most.";
            if (part.OnHand is < 0) return $"{name}: the count on hand can't be negative.";
            foreach (var url in new[] { part.InfoUrl, part.BuyUrl })
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (url.Trim().Length > StoreProductPart.MaxUrlLength) return $"{name}: a link is {StoreProductPart.MaxUrlLength} characters at most.";
                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                    return $"{name}: a link starts with https:// — {url.Trim()} doesn't.";
            }
        }
        if (other < 0m) return "The other costs can't be negative.";
        if (!FourPlaces(other)) return "The other costs go to the hundredth of a cent at most.";
        if (note?.Trim().Length > 300) return "What the other costs cover is 300 characters at most.";
        return null;
    }

    /// <summary>A part's picture — replacing the one it had.</summary>
    public async Task<StoreEditRefusal?> SetPartPictureAsync(
        BenDataContext db, Guid productId, Guid partId, byte[] bytes, string? contentType, string? fileName, StoreEditActor actor, CancellationToken ct)
    {
        var part = await db.StoreProductParts.FirstOrDefaultAsync(x => x.Id == partId && x.ProductId == productId, ct);
        if (part is null) return StoreEditRefusal.NotFound;
        if (bytes.Length == 0) return StoreEditRefusal.BadRequest("There was no picture in that upload.");

        var (stored, refusal) = await images.SaveAsync(db, bytes, contentType, fileName, $"products/{productId:N}/parts", actor.UserId, ct);
        if (refusal == StoreImageRefusal.NotAPicture) return StoreEditRefusal.BadRequest("A part's picture is a photograph — JPEG, PNG or similar.");
        if (stored is null) return StoreEditRefusal.BadRequest("That picture could not be read.");

        var old = part.ThumbnailUploadFileId;
        part.ThumbnailUploadFileId = stored.Id;
        part.DateUpdated = DateTime.UtcNow;
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Parts, $"Added a picture of {part.Name}.", actor.UserId, actor.Role, part.DateUpdated.Value);
        await db.SaveChangesAsync(ct);
        if (old is { } gone) await images.RemoveAsync(db, gone, ct);
        return null;
    }

    public async Task<StoreEditRefusal?> RemovePartPictureAsync(
        BenDataContext db, Guid productId, Guid partId, StoreEditActor actor, CancellationToken ct)
    {
        var part = await db.StoreProductParts.FirstOrDefaultAsync(x => x.Id == partId && x.ProductId == productId, ct);
        if (part is null) return StoreEditRefusal.NotFound;
        if (part.ThumbnailUploadFileId is not { } file) return null;

        part.ThumbnailUploadFileId = null;
        part.DateUpdated = DateTime.UtcNow;
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Parts, $"Removed the picture of {part.Name}.", actor.UserId, actor.Role, part.DateUpdated.Value);
        await db.SaveChangesAsync(ct);
        await images.RemoveAsync(db, file, ct);
        return null;
    }

    // ── files (P11) ──────────────────────────────────────────────────────────

    public const int MaxFiles = 50;

    private static string? FileProblem(string? title, string? version)
    {
        var t = title?.Trim();
        if (string.IsNullOrEmpty(t)) return "Give the file a title — it's what buyers see.";
        if (t.Length > StoreProductFile.MaxTitleLength) return $"A title is {StoreProductFile.MaxTitleLength} characters at most.";
        if (version?.Trim().Length > StoreProductFile.MaxVersionLength) return $"A version is {StoreProductFile.MaxVersionLength} characters at most.";
        return null;
    }

    private static string Who(StoreFileAudience audience) => audience == StoreFileAudience.Buyers ? "for buyers" : "private";

    /// <summary>An uploaded file for a product, up to <see cref="StoreProductFile.MaxBytes"/>.</summary>
    public async Task<StoreEditRefusal?> AddFileAsync(BenDataContext db, Guid productId, Stream content, long length, string? contentType,
        string fileName, SaveStoreProductFileRequest meta, StoreEditActor actor, CancellationToken ct)
    {
        if (length <= 0) return StoreEditRefusal.BadRequest("There was no file in that upload.");
        if (length > StoreProductFile.MaxBytes) return StoreEditRefusal.BadRequest("That file is too large — 95 MB at most.");
        if (FileProblem(meta.Title, meta.VersionLabel) is { } problem) return StoreEditRefusal.BadRequest(problem);
        if (await db.StoreProductFiles.CountAsync(f => f.ProductId == productId, ct) >= MaxFiles)
            return StoreEditRefusal.BadRequest($"A product carries {MaxFiles} files at most.");

        var stored = await images.SaveFileAsync(db, content, length, contentType, fileName, $"products/{productId:N}/files", actor.UserId, ct);
        var now = DateTime.UtcNow;
        db.StoreProductFiles.Add(new StoreProductFile
        {
            Id = Guid.NewGuid(), ProductId = productId, Kind = meta.Kind, Audience = meta.Audience, Title = meta.Title.Trim(),
            VersionLabel = Trimmed(meta.VersionLabel), UploadFileId = stored.Id, FileName = stored.FileName, SizeBytes = length,
            SortOrder = await NextFileOrderAsync(db, productId, ct), DateCreated = now, CreatedByAppUserId = actor.UserId,
        });
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Files, $"Added {meta.Kind.ToString().ToLowerInvariant()} “{meta.Title.Trim()}” ({Who(meta.Audience)}).",
            actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>A manual written on the site — or imported from Markdown or plain text — kept as sanitised HTML.</summary>
    public async Task<StoreEditRefusal?> AddManualAsync(BenDataContext db, Guid productId, SaveStoreManualRequest request, StoreEditActor actor, CancellationToken ct)
    {
        if (FileProblem(request.Title, request.VersionLabel) is { } problem) return StoreEditRefusal.BadRequest(problem);
        if (string.IsNullOrWhiteSpace(request.Body)) return StoreEditRefusal.BadRequest("The manual is empty.");
        if (await db.StoreProductFiles.CountAsync(f => f.ProductId == productId, ct) >= MaxFiles)
            return StoreEditRefusal.BadRequest($"A product carries {MaxFiles} files at most.");
        var html = sanitizer.SanitizeHtml(request.IsMarkdown ? Markdig.Markdown.ToHtml(request.Body) : request.Body);
        if (html.Length > 500_000) return StoreEditRefusal.BadRequest("A written manual is 500,000 characters at most — upload a PDF instead.");

        var now = DateTime.UtcNow;
        db.StoreProductFiles.Add(new StoreProductFile
        {
            Id = Guid.NewGuid(), ProductId = productId, Kind = StoreProductFileKind.Manual, Audience = request.Audience,
            Title = request.Title.Trim(), VersionLabel = Trimmed(request.VersionLabel), ManualHtml = html,
            SortOrder = await NextFileOrderAsync(db, productId, ct), DateCreated = now, CreatedByAppUserId = actor.UserId,
        });
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Files, $"Wrote the manual “{request.Title.Trim()}” ({Who(request.Audience)}).",
            actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>A file's title, kind, version, order and — most of all — who may have it.</summary>
    public static async Task<StoreEditRefusal?> UpdateFileAsync(BenDataContext db, Guid productId, Guid fileId, SaveStoreProductFileRequest meta,
        StoreEditActor actor, CancellationToken ct)
    {
        var file = await db.StoreProductFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProductId == productId, ct);
        if (file is null) return StoreEditRefusal.NotFound;
        if (FileProblem(meta.Title, meta.VersionLabel) is { } problem) return StoreEditRefusal.BadRequest(problem);

        var said = new List<string>();
        if (file.Audience != meta.Audience) said.Add(meta.Audience == StoreFileAudience.Buyers ? $"Gave buyers “{meta.Title.Trim()}”." : $"Made “{meta.Title.Trim()}” private.");
        if (file.Title != meta.Title.Trim() || file.VersionLabel != Trimmed(meta.VersionLabel) || file.Kind != meta.Kind)
            said.Add($"Changed the file “{meta.Title.Trim()}”.");
        if (said.Count == 0 && file.SortOrder != meta.SortOrder) said.Add($"Moved the file “{file.Title}”.");
        if (said.Count == 0) return null;
        file.Title = meta.Title.Trim();
        file.Kind = file.ManualHtml is null ? meta.Kind : StoreProductFileKind.Manual;
        file.Audience = meta.Audience;
        file.VersionLabel = Trimmed(meta.VersionLabel);
        file.SortOrder = meta.SortOrder;
        file.DateUpdated = DateTime.UtcNow;
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Files, string.Join(" ", said), actor.UserId, actor.Role, file.DateUpdated.Value);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<StoreEditRefusal?> DeleteFileAsync(BenDataContext db, Guid productId, Guid fileId, StoreEditActor actor, CancellationToken ct)
    {
        var file = await db.StoreProductFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.ProductId == productId, ct);
        if (file is null) return StoreEditRefusal.NotFound;
        db.StoreProductFiles.Remove(file);
        StoreProductHistory.Record(db, productId, StoreProductChangeArea.Files, $"Removed the file “{file.Title}”.", actor.UserId, actor.Role, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        if (file.UploadFileId is { } bytes) await images.RemoveAsync(db, bytes, ct);
        return null;
    }

    private static async Task<int> NextFileOrderAsync(BenDataContext db, Guid productId, CancellationToken ct)
        => (await db.StoreProductFiles.Where(f => f.ProductId == productId).MaxAsync(f => (int?)f.SortOrder, ct) ?? -1) + 1;

    // ── FAQ (P12) ────────────────────────────────────────────────────────────

    public const int MaxFaqs = 30;

    /// <summary>
    /// The FAQ, saved whole, and its switch. One history line says what changed; an unchanged save
    /// writes nothing.
    /// </summary>
    public static async Task<StoreEditRefusal?> SaveFaqsAsync(BenDataContext db, StoreProduct product, SaveStoreFaqsRequest request,
        StoreEditActor actor, CancellationToken ct)
    {
        if (request.Faqs.Count > MaxFaqs) return StoreEditRefusal.BadRequest($"An FAQ holds {MaxFaqs} entries at most.");
        foreach (var (line, n) in request.Faqs.Select((l, i) => (l, i + 1)))
        {
            if (string.IsNullOrWhiteSpace(line.Question) || string.IsNullOrWhiteSpace(line.Answer))
                return StoreEditRefusal.BadRequest($"Entry {n} needs both a question and an answer.");
            if (line.Question.Trim().Length > StoreProductFaq.MaxQuestionLength)
                return StoreEditRefusal.BadRequest($"Entry {n}'s question is {StoreProductFaq.MaxQuestionLength} characters at most.");
            if (line.Answer.Trim().Length > StoreProductFaq.MaxAnswerLength)
                return StoreEditRefusal.BadRequest($"Entry {n}'s answer is {StoreProductFaq.MaxAnswerLength} characters at most.");
        }

        var existing = await db.StoreProductFaqs.Where(f => f.ProductId == product.Id).ToListAsync(ct);
        var kept = request.Faqs.Where(l => l.Id is { } id && existing.Any(e => e.Id == id)).Select(l => l.Id!.Value).ToHashSet();
        var now = DateTime.UtcNow;
        int added = 0, changed = 0, removed = 0, moved = 0;

        foreach (var gone in existing.Where(e => !kept.Contains(e.Id)))
        {
            db.StoreProductFaqs.Remove(gone);
            removed++;
        }
        for (var i = 0; i < request.Faqs.Count; i++)
        {
            var line = request.Faqs[i];
            var (question, answer) = (line.Question.Trim(), line.Answer.Trim());
            if (line.Id is { } id && existing.FirstOrDefault(e => e.Id == id) is { } row)
            {
                if (row.Question != question || row.Answer != answer) { row.Question = question; row.Answer = answer; row.DateUpdated = now; changed++; }
                else if (row.SortOrder != i) moved++;
                row.SortOrder = i;
            }
            else
            {
                db.StoreProductFaqs.Add(new StoreProductFaq
                {
                    Id = Guid.NewGuid(), ProductId = product.Id, Question = question, Answer = answer, SortOrder = i,
                    DateCreated = now, CreatedByAppUserId = actor.UserId,
                });
                added++;
            }
        }

        var said = new List<string>();
        if (product.FaqEnabled != request.Enabled) said.Add(request.Enabled ? "Switched the FAQ on." : "Switched the FAQ off.");
        product.FaqEnabled = request.Enabled;
        var counts = new[] { (added, "added"), (changed, "changed"), (removed, "removed") }.Where(c => c.Item1 > 0)
            .Select(c => $"{c.Item2} {c.Item1}").ToList();
        if (counts.Count > 0) said.Add($"FAQ: {string.Join(", ", counts)}.");
        else if (moved > 0) said.Add("Reordered the FAQ.");
        if (said.Count == 0) return null;

        product.DateUpdated = now;
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Faq, string.Join(" ", said), actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ── versions (P13) ───────────────────────────────────────────────────────

    public const int MaxVersionLabelLength = 40;

    private static string? VersionLabelProblem(string? label)
    {
        var l = label?.Trim();
        if (string.IsNullOrEmpty(l)) return "Name the new version — “v2”, “2026 edition”.";
        return l.Length > MaxVersionLabelLength ? $"A version's name is {MaxVersionLabelLength} characters at most." : null;
    }

    /// <summary>
    /// Starts a new version of an item that has been on sale: a hidden draft copying everything but
    /// the stock, the same seller's, linked back to this one. One newer version per item.
    /// </summary>
    public async Task<(Guid? NewId, StoreEditRefusal? Refusal)> StartVersionAsync(
        BenDataContext db, StoreProduct source, StartStoreVersionRequest request, StoreEditActor actor, CancellationToken ct)
    {
        if (VersionLabelProblem(request.VersionLabel) is { } problem) return (null, StoreEditRefusal.BadRequest(problem));
        if (!Enum.IsDefined(request.Policy)) return (null, StoreEditRefusal.BadRequest("Choose what happens to this version."));
        if (source.FirstOnSaleUtc is null && source.UnitsSold == 0)
            return (null, StoreEditRefusal.BadRequest("This one hasn't been on sale yet — change the draft itself rather than starting a new version."));
        if (await db.StoreProducts.AsNoTracking().Where(p => p.PreviousVersionProductId == source.Id).Select(p => new { p.Name, p.VersionLabel }).FirstOrDefaultAsync(ct) is { } newer)
            return (null, StoreEditRefusal.Conflict($"There's a newer version already: “{newer.Name}”{(newer.VersionLabel is { } l ? $" ({l})" : "")}. Start the next one from that."));

        var label = request.VersionLabel.Trim();
        var suffix = "-" + new string(label.ToUpperInvariant().Where(char.IsLetterOrDigit).Take(10).ToArray());
        var now = DateTime.UtcNow;
        var copy = await StoreProductCopier.CopyAsync(db, images, source.Id,
            new StoreProductCopier.Plan(source.Name, suffix.Length > 1 ? suffix : "-NEW", Everything: true, source.SellerAppUserId,
                PreviousVersionProductId: source.Id, VersionLabel: label, Policy: request.Policy, SlugSource: $"{source.Name} {label}"),
            actor.UserId, now, ct);
        if (copy is null) return (null, StoreEditRefusal.NotFound);

        StoreProductHistory.Record(db, copy.Id, StoreProductChangeArea.Created,
            $"Started version {label} from “{source.Name}”{(source.VersionLabel is { } was ? $" ({was})" : "")}. When it goes on sale, the old one {PolicyWords(request.Policy)}.",
            actor.UserId, actor.Role, now);
        StoreProductHistory.Record(db, source.Id, StoreProductChangeArea.Versions, $"Started a new version, {label}, as a draft.", actor.UserId, actor.Role, now);
        await db.SaveChangesAsync(ct);
        await StorePriceCaches.RecomputeAsync(db, copy.Id, ct);
        return (copy.Id, null);
    }

    /// <summary>A version's label, and its policy until it has gone on sale (then it has happened, and is fixed).</summary>
    public static async Task<StoreEditRefusal?> SaveVersionAsync(
        BenDataContext db, StoreProduct product, SaveStoreVersionRequest request, StoreEditActor actor, CancellationToken ct)
    {
        var label = string.IsNullOrWhiteSpace(request.VersionLabel) ? null : request.VersionLabel.Trim();
        if (label?.Length > MaxVersionLabelLength) return StoreEditRefusal.BadRequest($"A version's name is {MaxVersionLabelLength} characters at most.");
        if (product.PreviousVersionProductId is not null && label is null) return StoreEditRefusal.BadRequest("A new version needs a name.");
        if (!Enum.IsDefined(request.Policy)) return StoreEditRefusal.BadRequest("Choose what happens to the previous version.");

        var said = new List<string>();
        if (label != product.VersionLabel) said.Add(label is null ? "Removed the version name." : $"Called this version {label}.");
        if (request.Policy != product.SupersededPolicy)
        {
            if (product.PreviousVersionProductId is null) return StoreEditRefusal.BadRequest("This is a first version — there's nothing before it.");
            if (product.SupersededAppliedUtc is not null) return StoreEditRefusal.Conflict("This version has gone on sale, and what happened to the old one has happened.");
            said.Add($"When this goes on sale, the old one {PolicyWords(request.Policy)}.");
        }
        if (said.Count == 0) return null;
        product.VersionLabel = label;
        product.SupersededPolicy = request.Policy;
        product.DateUpdated = DateTime.UtcNow;
        StoreProductHistory.Record(db, product.Id, StoreProductChangeArea.Versions, string.Join(" ", said), actor.UserId, actor.Role, product.DateUpdated.Value);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public static string PolicyWords(StoreSupersededPolicy policy) => policy switch
    {
        StoreSupersededPolicy.SellOut => "sells what's left, then comes off sale",
        StoreSupersededPolicy.Discontinue => "comes off sale straight away",
        _ => "stays on sale beside it",
    };

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static async Task<string> VariantLabelAsync(BenDataContext db, Guid variantId, CancellationToken ct)
        => StorePriceCaches.Label(await db.StoreProductVariants.AsNoTracking().Where(v => v.Id == variantId).Select(v => v.Name).FirstOrDefaultAsync(ct));

    private static int IndexOf(IReadOnlyList<Guid> ids, Guid id)
    {
        for (var i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
        return -1;
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static string? TruncateOrNull(string? s, int max) => s is null ? null : Truncate(s, max);
}
