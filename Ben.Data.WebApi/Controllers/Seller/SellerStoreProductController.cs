using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>The seller's own items: the list, and the counts at the top of the workspace.</summary>
[ApiController]
[Authorize(Policy = AuthPolicyNames.Seller)]
[Route("api/seller/store/products")]
public sealed class SellerStoreProductController(IDbContextFactory<BenDataContext> dbFactory) : SellerStoreControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SellerProductListRecord>>> GetMine(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await Mine(db).AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Slug,
                Category = p.Category.ParentCategory == null ? p.Category.Name : p.Category.ParentCategory.Name + " › " + p.Category.Name,
                p.IsActive, p.UnitsSold, p.LastSoldUtc, p.MinPrice, p.MaxPrice,
                Stock = p.Variants.Where(v => v.IsActive).Sum(v => v.StockOnHand),
                Image = p.Images.Where(i => i.VariantId == null).OrderBy(i => i.SortOrder).Select(i => (Guid?)i.UploadFileId).FirstOrDefault()
                        ?? p.Images.OrderBy(i => i.SortOrder).Select(i => (Guid?)i.UploadFileId).FirstOrDefault(),
                Updated = p.DateUpdated ?? p.DateCreated,
            })
            .ToListAsync(ct);

        return Ok(rows.Select(p => new SellerProductListRecord(
            p.Id, p.Name, p.Slug, p.Category, StoreSellerStatus.For(p.IsActive, p.UnitsSold > 0),
            p.MaxPrice > 0 ? p.MinPrice : null, p.MaxPrice > 0 ? p.MaxPrice : null,
            p.Stock, p.UnitsSold, p.LastSoldUtc, p.Image, p.Updated)).ToList());
    }

    [HttpGet("summary")]
    public async Task<ActionResult<SellerWorkspaceSummary>> Summary(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var states = await Mine(db).AsNoTracking().Select(p => new { p.IsActive, Sold = p.UnitsSold > 0 }).ToListAsync(ct);
        var statuses = states.Select(s => StoreSellerStatus.For(s.IsActive, s.Sold)).ToList();
        return Ok(new SellerWorkspaceSummary(
            statuses.Count(s => s == StoreSellerItemStatus.Draft),
            statuses.Count(s => s == StoreSellerItemStatus.OnSale),
            statuses.Count(s => s == StoreSellerItemStatus.OffSale)));
    }
}
