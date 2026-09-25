using System.Globalization;
using System.Text;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// Stock across the whole store at once: receive a delivery, correct a count, import a supplier's
/// sheet, or download the lot (storefront S1.4).
/// </summary>
/// <remarks>
/// <para><b>All or nothing.</b> A delivery is one event; a save that applied four lines and
/// refused the fifth would leave the shelf matching neither the delivery note nor what the admin
/// typed. <see cref="StoreProductHistory.AdjustStockAsync"/> runs every line in one transaction,
/// names the SKU that stopped it, and leaves each item a line in its history (store sellers P2).</para>
///
/// <para><b>Available is what matters.</b> On hand less what checkouts in progress are holding —
/// the same figure the product page's "Only 3 left" uses.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/stock")]
public sealed class AdminStoreStockController(IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    /// <summary>Largest import accepted. A sheet with more rows than the store has variants is a mistake.</summary>
    public const int MaxImportRows = 5_000;

    /// <summary>Every active variant, with what is on the shelf, held and free.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreStockRow>>> GetAll([FromQuery] string? q, [FromQuery] bool? low, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return Ok(await RowsAsync(db, q, low == true, ct));
    }

    /// <summary>Several variants at once — the stock page's one Save.</summary>
    [HttpPost("adjust")]
    public async Task<ActionResult<StoreStockAdjusted>> Adjust([FromBody] BulkAdjustStockRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        var lines = (request.Lines ?? []).Where(l => l.SetTo is not null || l.Delta is not null and not 0).ToList();
        if (lines.Count == 0) return BadRequest("Nothing was changed — no line had a quantity.");
        foreach (var line in lines)
            if (StoreProductEditor.StockRequestProblem(line.Delta, line.SetTo, request.Reason) is { } problem)
                return BadRequest(problem);
        if (lines.Select(l => l.VariantId).Distinct().Count() != lines.Count)
            return BadRequest("Nothing was changed — the same item is listed twice.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var refusal = await StoreProductHistory.AdjustStockAsync(db,
            lines.Select(l => new StoreStockChange(l.VariantId, l.Delta, l.SetTo)).ToList(),
            request.Reason, request.Note, userId, StoreChangeActor.Store, DateTime.UtcNow, ct);
        if (refusal is not null) return BadRequest(refusal);

        return Ok(new StoreStockAdjusted(lines.Count, await RowsAsync(db, null, false, ct)));
    }

    /// <summary>
    /// A CSV of <c>Sku,Delta</c> rows — a delivery note, typically — received all together or
    /// not at all. A header row is optional.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(5_000_000)]
    public async Task<ActionResult<StoreStockAdjusted>> Import(IFormFile? file, [FromForm] string? note, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (file is null || file.Length == 0) return BadRequest("There was no file in that upload.");

        string text;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await reader.ReadToEndAsync(ct);
        var rows = StoreCsv.Parse(text);
        if (rows.Count > 0 && rows[0].Count > 0 && rows[0][0].Trim().Equals("Sku", StringComparison.OrdinalIgnoreCase))
            rows.RemoveAt(0);
        if (rows.Count == 0) return BadRequest("That file has no rows — each line is a SKU and a change, like KII-EMF,12.");
        if (rows.Count > MaxImportRows) return BadRequest($"An import is {MaxImportRows:N0} rows at most.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var skus = await db.StoreProductVariants.AsNoTracking()
            .Select(v => new { v.Id, v.Sku }).ToDictionaryAsync(v => v.Sku, v => v.Id, StringComparer.OrdinalIgnoreCase, ct);

        var changes = new List<StoreStockChange>();
        var seen = new HashSet<Guid>();
        for (var i = 0; i < rows.Count; i++)
        {
            var n = i + 1;
            var sku = rows[i].ElementAtOrDefault(0)?.Trim() ?? string.Empty;
            var raw = rows[i].ElementAtOrDefault(1)?.Trim() ?? string.Empty;
            if (!skus.TryGetValue(sku, out var variantId)) return BadRequest($"Row {n}: no variant has SKU {sku}.");
            if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta))
                return BadRequest($"Row {n}: {(raw.Length == 0 ? "the change is missing" : $"{raw} isn't a whole number")}.");
            if (!seen.Add(variantId)) return BadRequest($"Row {n}: {sku} is listed twice.");
            if (delta != 0) changes.Add(new StoreStockChange(variantId, delta, null));
        }
        if (changes.Count == 0) return BadRequest("Nothing was changed — every row's change was 0.");

        var refusal = await StoreProductHistory.AdjustStockAsync(db, changes, StoreStockReason.Received,
            string.IsNullOrWhiteSpace(note) ? $"Imported from {Path.GetFileName(file.FileName)}" : note, userId, StoreChangeActor.Store, DateTime.UtcNow, ct);
        if (refusal is not null) return BadRequest(refusal);

        return Ok(new StoreStockAdjusted(changes.Count, await RowsAsync(db, null, false, ct)));
    }

    /// <summary>Every active variant's stock as a CSV file.</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await RowsAsync(db, null, false, ct);

        var lines = new List<string> { StoreCsv.Line("Sku", "Product", "Variant", "OnHand", "Reserved", "Available", "Low") };
        lines.AddRange(rows.Select(r => StoreCsv.Line(r.Sku, r.ProductName, r.VariantName, r.OnHand, r.Reserved, r.Available, r.IsLow ? "yes" : "no")));

        return File(StoreCsv.Bytes(lines), "text/csv", $"store-stock-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    internal static async Task<List<StoreStockRow>> RowsAsync(BenDataContext db, string? q, bool lowOnly, CancellationToken ct)
    {
        var threshold = (await StoreSettingsReader.ReadAsync(db, ct)).LowStockThreshold;
        var query = db.StoreProductVariants.AsNoTracking().Where(v => v.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(v => v.Sku.ToLower().Contains(term) || v.Product.Name.ToLower().Contains(term)
                                  || (v.Name != null && v.Name.ToLower().Contains(term)));
        }
        if (lowOnly) query = query.Where(v => v.StockOnHand - v.StockReserved <= threshold);

        var rows = await query
            .OrderBy(v => v.Product.Name).ThenBy(v => v.SortOrder).ThenBy(v => v.Sku)
            .Select(v => new { v.Id, v.ProductId, Product = v.Product.Name, v.Sku, v.Name, v.StockOnHand, v.StockReserved })
            .ToListAsync(ct);

        return rows.Select(v => new StoreStockRow(
            v.Id, v.ProductId, v.Product, v.Sku, StorePriceCaches.Label(v.Name), v.StockOnHand, v.StockReserved,
            v.StockOnHand - v.StockReserved, v.StockOnHand - v.StockReserved <= threshold)).ToList();
    }
}
