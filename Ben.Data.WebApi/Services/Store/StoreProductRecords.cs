using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// An item as its editors read it — the store's and, since store sellers P3, the seller's — and
/// the sale requests the queue lists.
/// </summary>
public static class StoreProductRecords
{
    /// <summary>The whole item: details, options, variants, pictures, where it stands on sale.</summary>
    public static async Task<StoreProductAdminRecord?> LoadAsync(BenDataContext db, Guid id, CancellationToken ct)
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
        var open = (await RequestsAsync(db, db.StoreProductSaleRequests.Where(r => r.ProductId == id && r.Status == StoreSaleRequestStatus.Open), ct))
            .FirstOrDefault();

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
            $"/store/p/{p.Slug}", p.DateCreated, p.DateUpdated, p.SellerAppUserId, sellerName,
            p.SellerAskPerUnit, p.FirstOnSaleUtc, open);
    }

    /// <summary>
    /// One of a seller's items for their editor: the item, where it stands, and the request that is
    /// waiting — or, when none is, the last one the store answered.
    /// </summary>
    public static async Task<SellerItemRecord?> LoadForSellerAsync(BenDataContext db, Guid id, CancellationToken ct)
    {
        if (await LoadAsync(db, id, ct) is not { } item) return null;
        var request = item.OpenSaleRequest
            ?? (await RequestsAsync(db, db.StoreProductSaleRequests
                .Where(r => r.ProductId == id && (r.Status == StoreSaleRequestStatus.Declined || r.Status == StoreSaleRequestStatus.Approved))
                .OrderByDescending(r => r.DecidedUtc).Take(1), ct)).FirstOrDefault();
        return new SellerItemRecord(item, StoreSellerStatus.For(item.IsActive, item.FirstOnSaleUtc is not null || item.UnitsSold > 0),
            item.SellerAskPerUnit, request);
    }

    /// <summary>An item's parts and what a unit costs to make (P4). The seller's and the store's; never a shopper's.</summary>
    public static async Task<StorePartsRecord?> PartsAsync(BenDataContext db, Guid productId, CancellationToken ct)
    {
        var product = await db.StoreProducts.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new { p.OtherCostPerUnit, p.OtherCostNote, Edited = p.DateUpdated ?? p.DateCreated }).FirstOrDefaultAsync(ct);
        if (product is null) return null;
        var parts = await db.StoreProductParts.AsNoTracking().Where(x => x.ProductId == productId).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var records = parts.Select(x => new StorePartRecord(
            x.Id, x.Name, x.PriceBasis, x.Price, x.PiecesPerPack, x.QuantityPerUnit, x.InfoUrl, x.BuyUrl, x.ThumbnailUploadFileId, x.OnHand,
            x.SortOrder, StoreCostMath.PartCostPerUnit(x.PriceBasis, x.Price, x.PiecesPerPack, x.QuantityPerUnit))).ToList();
        return new StorePartsRecord(productId, records, product.OtherCostPerUnit, product.OtherCostNote,
            StoreCostMath.CostBasis(records.Select(r => r.CostPerUnit), product.OtherCostPerUnit),
            StoreCostMath.Buildable(records.Select(r => (r.OnHand, r.QuantityPerUnit))), product.Edited);
    }

    /// <summary>A product's files, in their order (P11).</summary>
    public static Task<List<StoreProductFileRecord>> FilesAsync(BenDataContext db, Guid productId, CancellationToken ct)
        => db.StoreProductFiles.AsNoTracking().Where(f => f.ProductId == productId).OrderBy(f => f.SortOrder).ThenBy(f => f.DateCreated)
            .Select(f => new StoreProductFileRecord(f.Id, f.Kind, f.Audience, f.Title, f.VersionLabel, f.FileName, f.SizeBytes,
                f.ManualHtml != null, f.SortOrder, f.DateUpdated ?? f.DateCreated))
            .ToListAsync(ct);

    /// <summary>The requests <paramref name="query"/> selects, newest first, each with what its item still needs.</summary>
    public static async Task<List<StoreSaleRequestRecord>> RequestsAsync(
        BenDataContext db, IQueryable<StoreProductSaleRequest> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .OrderByDescending(r => r.RequestedUtc)
            .Select(r => new
            {
                r.Id, r.ProductId, Product = r.Product.Name, r.SellerAppUserId,
                Seller = r.SellerAppUser.DisplayName ?? "A seller",
                r.SellerAskingPrice, r.SellerNote, r.Status, r.RequestedUtc, r.DecidedUtc, r.DecisionNote,
                r.Product.MinPrice, r.Product.MaxPrice,
                Image = r.Product.Images.OrderBy(i => i.VariantId != null).ThenBy(i => i.SortOrder).Select(i => (Guid?)i.UploadFileId).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var records = new List<StoreSaleRequestRecord>(rows.Count);
        foreach (var r in rows)
            records.Add(new StoreSaleRequestRecord(
                r.Id, r.ProductId, r.Product, r.SellerAppUserId, r.Seller, r.SellerAskingPrice, r.SellerNote, r.Status,
                r.RequestedUtc, r.DecidedUtc, r.DecisionNote, r.MinPrice, r.MaxPrice,
                r.Status == StoreSaleRequestStatus.Open ? await StoreProductSale.ProblemsAsync(db, r.ProductId, ct) : [],
                r.Image));
        return records;
    }
}
