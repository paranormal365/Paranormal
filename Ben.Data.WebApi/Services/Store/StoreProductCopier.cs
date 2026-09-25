using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Makes a hidden copy of a product (storefront's Duplicate, and since store sellers P13 a new
/// version): words, category, options, variants (new SKUs, no stock), specs and pictures (new files).
/// A new version also takes the parts list, the FAQ and the files — and stays the same seller's.
/// </summary>
/// <remarks>
/// Adds rows and returns the copy <b>unsaved</b>: the caller records the history line and saves, so
/// the copy and the sentence about it land together, then recomputes the price caches.
/// </remarks>
public static class StoreProductCopier
{
    /// <param name="Name">The copy's name.</param>
    /// <param name="SkuSuffix">Appended to each SKU ("-COPY", "-V2"); a clash gets a number as well.</param>
    /// <param name="Everything">Parts, FAQ and files too — a new version's copy.</param>
    public sealed record Plan(string Name, string SkuSuffix, bool Everything, Guid? SellerAppUserId,
        Guid? PreviousVersionProductId = null, string? VersionLabel = null, StoreSupersededPolicy Policy = StoreSupersededPolicy.KeepOffering,
        string? SlugSource = null);

    public static async Task<StoreProduct?> CopyAsync(BenDataContext db, StoreImageStorage images, Guid sourceId, Plan plan, Guid userId, DateTime now, CancellationToken ct)
    {
        var source = await db.StoreProducts.AsNoTracking()
            .Include(p => p.Options).ThenInclude(o => o.Values)
            .Include(p => p.Variants).ThenInclude(v => v.OptionValues)
            .Include(p => p.Specs).Include(p => p.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == sourceId, ct);
        if (source is null) return null;

        var copy = new StoreProduct
        {
            Id = Guid.NewGuid(), CategoryId = source.CategoryId, EquipmentModelId = source.EquipmentModelId,
            Name = StoreProductEditor.Truncate(plan.Name, StoreProductEditor.MaxNameLength), ShortDescription = source.ShortDescription,
            LongDescriptionHtml = source.LongDescriptionHtml, IsActive = false, IsFeatured = false,
            SortOrder = source.SortOrder + 1, StripeTaxCode = source.StripeTaxCode,
            SellerAppUserId = plan.SellerAppUserId,
            PreviousVersionProductId = plan.PreviousVersionProductId, VersionLabel = plan.VersionLabel, SupersededPolicy = plan.Policy,
            FaqEnabled = source.FaqEnabled,
            OtherCostPerUnit = plan.Everything ? source.OtherCostPerUnit : 0m, OtherCostNote = plan.Everything ? source.OtherCostNote : null,
            DateCreated = now, CreatedByAppUserId = userId,
        };
        copy.Slug = await StoreSlugs.ForProductAsync(db, copy.Id, plan.SlugSource ?? copy.Name, ct);
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
            var sku = await StoreProductEditor.UniqueSkuAsync(db, StoreProductEditor.Truncate(variant.Sku, 64 - plan.SkuSuffix.Length) + plan.SkuSuffix, ct, takenSkus);
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

        if (!plan.Everything) return copy;

        // A new version's parts (their pictures are shared: a part's picture is the same part's),
        // its FAQ, and its files — the same stored bytes, which are only removed when nothing holds them.
        foreach (var part in await db.StoreProductParts.AsNoTracking().Where(x => x.ProductId == sourceId).ToListAsync(ct))
            db.StoreProductParts.Add(new StoreProductPart
            {
                Id = Guid.NewGuid(), ProductId = copy.Id, Name = part.Name, PriceBasis = part.PriceBasis, Price = part.Price,
                PiecesPerPack = part.PiecesPerPack, QuantityPerUnit = part.QuantityPerUnit, InfoUrl = part.InfoUrl, BuyUrl = part.BuyUrl,
                ThumbnailUploadFileId = part.ThumbnailUploadFileId, OnHand = part.OnHand, SortOrder = part.SortOrder, DateCreated = now,
            });
        foreach (var faq in await db.StoreProductFaqs.AsNoTracking().Where(x => x.ProductId == sourceId).ToListAsync(ct))
            db.StoreProductFaqs.Add(new StoreProductFaq
            {
                Id = Guid.NewGuid(), ProductId = copy.Id, Question = faq.Question, Answer = faq.Answer, SortOrder = faq.SortOrder,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        foreach (var file in await db.StoreProductFiles.AsNoTracking().Where(x => x.ProductId == sourceId).ToListAsync(ct))
            db.StoreProductFiles.Add(new StoreProductFile
            {
                Id = Guid.NewGuid(), ProductId = copy.Id, Kind = file.Kind, Audience = file.Audience, Title = file.Title,
                VersionLabel = file.VersionLabel, UploadFileId = file.UploadFileId, ManualHtml = file.ManualHtml, FileName = file.FileName,
                SizeBytes = file.SizeBytes, SortOrder = file.SortOrder, DateCreated = now, CreatedByAppUserId = userId,
            });
        return copy;
    }
}
