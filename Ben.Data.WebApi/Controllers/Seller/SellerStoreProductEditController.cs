using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Seller;

/// <summary>
/// A seller changing their own items (store sellers, backlog 251, P3): drafts, words, pictures,
/// options, variants and stock; asking for an item to go on sale, taking the request back, and
/// taking an item off sale.
/// </summary>
/// <remarks>
/// <para><b>The admin's rules, less the store's fields.</b> Every change goes through
/// <see cref="StoreProductEditor"/>, as the admin's do. The requests here have no price, web
/// address, placement, featuring, tax or seller to send, and the editor keeps a variant's price
/// whatever arrives (SellerCannotPriceTests). A new variant starts off and unpriced.</para>
///
/// <para><b>On sale only by asking.</b> A seller cannot put an item on sale; they ask, with what
/// they want a unit, and the store prices and approves it. They can take one off at any time — the
/// store is told.</para>
///
/// <para>Every answer is the item as the seller's editor reads it, so the page never has to guess
/// what a save did.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = AuthPolicyNames.Seller)]
[Route("api/seller/store/products")]
public sealed class SellerStoreProductEditController(
    IDbContextFactory<BenDataContext> dbFactory, StoreImageStorage images, ICmsMarkupSanitizer sanitizer, StoreSellerAlerts alerts)
    : SellerStoreControllerBase
{
    private readonly StoreProductEditor _editor = new(sanitizer, images);

    private static async Task<ActionResult<SellerItemRecord>> ItemAsync(BenDataContext db, Guid id, CancellationToken ct)
        => new OkObjectResult(await StoreProductRecords.LoadForSellerAsync(db, id, ct));

    /// <summary>A new draft from a name: hidden, the caller's, with one variant for the store to price.</summary>
    [HttpPost]
    public async Task<ActionResult<SellerItemRecord>> Create([FromBody] CreateSellerItemRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (product, refusal) = await _editor.CreateAsync(db, request.Name, request.CategoryId, Seller, ct);
        if (refusal is not null) return this.Refused(refusal);
        return await ItemAsync(db, product!.Id, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SellerItemRecord>> Update(Guid id, [FromBody] SaveSellerItemRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        var refusal = await _editor.SaveDetailsAsync(db, product, new StoreDetailsEdit(
            request.Name, request.CategoryId, request.ShortDescription, request.LongDescriptionHtml, request.Specs,
            request.ExpectedDateUpdated, Admin: null), Seller, ct);
        if (refusal is not null) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    /// <summary>Deletes a draft that has never been on sale. Anything else is taken off sale instead.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (await _editor.DeleteAsync(db, product, Seller, ct) is { } refusal) return this.Refused(refusal);
        return NoContent();
    }

    // ── going on sale ────────────────────────────────────────────────────────

    /// <summary>Asks the store to put the item on sale, saying what the seller wants a unit. The store's admins are told.</summary>
    [HttpPost("{id:guid}/sale-request")]
    public async Task<ActionResult<SellerItemRecord>> RequestSale(Guid id, [FromBody] SellerSaleRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        var (_, refusal) = await StoreProductSale.RequestAsync(db, product, request.AskingPrice, request.Note, Seller, ct);
        if (refusal is not null) return this.Refused(refusal);
        await alerts.SaleRequestedAsync(id, ct);
        return await ItemAsync(db, id, ct);
    }

    /// <summary>Takes back the request that is waiting.</summary>
    [HttpDelete("{id:guid}/sale-request")]
    public async Task<ActionResult<SellerItemRecord>> WithdrawSale(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (await StoreProductSale.WithdrawAsync(db, id, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    /// <summary>Takes the item off sale. It stays, hidden; going back on sale needs a new request.</summary>
    [HttpPost("{id:guid}/off-sale")]
    public async Task<ActionResult<SellerItemRecord>> TakeOffSale(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (!product.IsActive) return BadRequest("It isn't on sale.");
        if (await _editor.SwitchAsync(db, product, on: false, Seller, ct) is { } refusal) return this.Refused(refusal);
        await alerts.TakenOffSaleAsync(id, ct);
        return await ItemAsync(db, id, ct);
    }

    // ── options and variants ─────────────────────────────────────────────────

    [HttpPut("{id:guid}/options")]
    public async Task<ActionResult<SellerItemRecord>> SaveOptions(Guid id, [FromBody] SaveStoreOptionsRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (await _editor.SaveOptionsAsync(db, id, request.Options, request.ExpectedDateUpdated, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    /// <summary>A variant for each new combination of choices — off and unpriced until the store prices them.</summary>
    [HttpPost("{id:guid}/variants/generate")]
    public async Task<ActionResult<SellerItemRecord>> GenerateVariants(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        var (_, refusal) = await _editor.GenerateVariantsAsync(db, id, Seller, ct);
        if (refusal is not null) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    [HttpPost("{id:guid}/variants")]
    public async Task<ActionResult<SellerItemRecord>> CreateVariant(Guid id, [FromBody] SaveSellerVariantRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (await _editor.CreateVariantAsync(db, product, Edit(request), Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<SellerItemRecord>> UpdateVariant(
        Guid id, Guid variantId, [FromBody] SaveSellerVariantRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (await _editor.UpdateVariantAsync(db, product, variantId, Edit(request), Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    [HttpDelete("{id:guid}/variants/{variantId:guid}")]
    public async Task<ActionResult<SellerItemRecord>> DeleteVariant(Guid id, Guid variantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (await _editor.DeleteVariantAsync(db, product, variantId, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    /// <summary>No price travels: the editor keeps the store's (or $0.00 for a new variant).</summary>
    private static StoreVariantEdit Edit(SaveSellerVariantRequest r)
        => new(r.Sku, Price: null, CompareAtPrice: null, r.IsActive, r.IsDefault, r.SortOrder, r.OptionValueIds, r.InitialStock);

    // ── stock ────────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/variants/{variantId:guid}/stock")]
    public async Task<ActionResult<SellerItemRecord>> AdjustStock(
        Guid id, Guid variantId, [FromBody] AdjustStockRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (await StoreProductEditor.AdjustStockAsync(db, id, variantId, request, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    // ── parts and cost (P4) ──────────────────────────────────────────────────

    [HttpGet("{id:guid}/parts")]
    public async Task<ActionResult<StorePartsRecord>> Parts(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/parts")]
    public async Task<ActionResult<StorePartsRecord>> SaveParts(Guid id, [FromBody] SaveStorePartsRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (await _editor.SavePartsAsync(db, product, request, Seller, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    [HttpPost("{id:guid}/parts/{partId:guid}/picture")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<StorePartsRecord>> SetPartPicture(Guid id, Guid partId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);
        if (await _editor.SetPartPictureAsync(db, id, partId, buffer.ToArray(), file.ContentType, file.FileName, Seller, ct) is { } refusal)
            return this.Refused(refusal);
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/parts/{partId:guid}/picture")]
    public async Task<ActionResult<StorePartsRecord>> RemovePartPicture(Guid id, Guid partId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (await _editor.RemovePartPictureAsync(db, id, partId, Seller, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.PartsAsync(db, id, ct));
    }

    // ── files (store sellers P11) ────────────────────────────────────────────

    [HttpGet("{id:guid}/files")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> Files(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Mine(db).AnyAsync(p => p.Id == id, ct)) return NotFound();
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpPost("{id:guid}/files")]
    [RequestSizeLimit(100_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100_000_000)]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> AddFile(Guid id, IFormFile? file, [FromForm] string? title,
        [FromForm] StoreProductFileKind kind, [FromForm] StoreFileAudience audience, [FromForm] string? versionLabel, CancellationToken ct)
    {
        var me = Seller;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Mine(db).AnyAsync(p => p.Id == id, ct)) return NotFound();
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
        var me = Seller;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Mine(db).AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await _editor.AddManualAsync(db, id, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpPut("{id:guid}/files/{fileId:guid}")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> SaveFile(Guid id, Guid fileId, [FromBody] SaveStoreProductFileRequest request, CancellationToken ct)
    {
        var me = Seller;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Mine(db).AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await StoreProductEditor.UpdateFileAsync(db, id, fileId, request, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    [HttpDelete("{id:guid}/files/{fileId:guid}")]
    public async Task<ActionResult<IEnumerable<StoreProductFileRecord>>> DeleteFile(Guid id, Guid fileId, CancellationToken ct)
    {
        var me = Seller;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Mine(db).AnyAsync(p => p.Id == id, ct)) return NotFound();
        if (await _editor.DeleteFileAsync(db, id, fileId, me, ct) is { } refusal) return this.Refused(refusal);
        return Ok(await StoreProductRecords.FilesAsync(db, id, ct));
    }

    // ── pictures ─────────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/images")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<SellerItemRecord>> AddImage(
        Guid id, IFormFile? file, [FromForm] string? altText, [FromForm] Guid? variantId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (file is null || file.Length == 0) return BadRequest("There was no picture in that upload.");

        using var buffer = new MemoryStream();
        await using (var stream = file.OpenReadStream()) await stream.CopyToAsync(buffer, ct);
        if (await _editor.AddImageAsync(db, id, buffer.ToArray(), file.ContentType, file.FileName, altText, variantId, Seller, ct) is { } refusal)
            return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    [HttpPut("{id:guid}/images/reorder")]
    public async Task<ActionResult<SellerItemRecord>> ReorderImages(Guid id, [FromBody] ReorderRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (await StoreProductEditor.ReorderImagesAsync(db, id, request.OrderedIds, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    [HttpPut("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<SellerItemRecord>> UpdateImage(
        Guid id, Guid imageId, [FromBody] UpdateStoreImageRequest request, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is null) return NotFound();
        if (await StoreProductEditor.UpdateImageAsync(db, id, imageId, request, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    public async Task<ActionResult<SellerItemRecord>> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await MineAsync(db, id, ct) is not { } product) return NotFound();
        if (await _editor.DeleteImageAsync(db, product, imageId, Seller, ct) is { } refusal) return this.Refused(refusal);
        return await ItemAsync(db, id, ct);
    }
}
