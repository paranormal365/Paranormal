using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>The seller's own items: the list, the counts at the top of the workspace, and each item's history.</summary>
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

    /// <summary>
    /// Who changed what on one of the caller's items, newest first (store sellers P2) — without
    /// the store's price changes, and with the store's staff as "The store".
    /// </summary>
    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IEnumerable<StoreProductChangeRecord>>> History(
        Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Mine(db).AnyAsync(p => p.Id == id, ct)) return NotFound();
        var me = GetCurrentUserIdOrThrow();
        return Ok(ListPaging.Apply(await StoreProductHistory.ReadAsync(db, id, forSeller: me, ct), page, pageSize, Response));
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
