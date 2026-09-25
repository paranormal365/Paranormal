using System.Text;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The store's sellers and what it owes them (store sellers, backlog 251, P10): balances, one
/// seller's earnings, recording a payment made outside the site, voiding one, adjustments, and a CSV.
/// </summary>
/// <remarks>
/// Payouts are manual (Ben, 09/24/2026): the site is the merchant of record and takes the whole
/// payment; an admin pays a seller by bank transfer or check and records it here, which marks the
/// earnings it covers paid. A payment is for what the page showed — if earnings changed while it was
/// open, recording is refused with the new figure.
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/sellers")]
public sealed class AdminStoreSellerController(IDbContextFactory<BenDataContext> dbFactory, TimeProvider? clock = null) : BenControllerBase
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    /// <summary>Everybody who sells, or has earnings on the books: what each is owed and has been paid.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StoreSellerBalanceRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var cutoff = StoreSellerLedger.DefaultCutoff(Now, settings.ReturnsWindowDays);
        var roleHolders = StoreProductEditor.SellerIds(db);
        var ids = await db.AppUsers.AsNoTracking()
            .Where(u => roleHolders.Contains(u.Id) || db.StoreSellerEarnings.Any(e => e.SellerAppUserId == u.Id))
            .Select(u => u.Id).ToListAsync(ct);

        var people = await db.AppUsers.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(ct);
        var earnings = await db.StoreSellerEarnings.AsNoTracking().Where(e => ids.Contains(e.SellerAppUserId))
            .Select(e => new { e.SellerAppUserId, e.Amount, e.PayoutId, e.OccurredUtc }).ToListAsync(ct);
        var payouts = await db.StoreSellerPayouts.AsNoTracking().Where(p => ids.Contains(p.SellerAppUserId) && p.VoidedUtc == null)
            .Select(p => new { p.SellerAppUserId, p.Amount, p.PaidOnUtc }).ToListAsync(ct);
        var onSale = await db.StoreProducts.AsNoTracking().Where(p => p.SellerAppUserId != null && ids.Contains(p.SellerAppUserId.Value) && p.IsActive)
            .GroupBy(p => p.SellerAppUserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        return Ok(people.Select(u =>
        {
            var mine = earnings.Where(e => e.SellerAppUserId == u.Id && e.PayoutId == null).ToList();
            var paid = payouts.Where(p => p.SellerAppUserId == u.Id).ToList();
            return new StoreSellerBalanceRecord(u.Id, u.DisplayName ?? u.Email ?? "A seller", u.Email,
                mine.Sum(e => e.Amount), mine.Where(e => e.OccurredUtc <= cutoff).Sum(e => e.Amount), paid.Sum(p => p.Amount),
                paid.Count == 0 ? null : paid.Max(p => p.PaidOnUtc), onSale.GetValueOrDefault(u.Id));
        }).OrderByDescending(s => s.PayableNow).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    [HttpGet("{sellerId:guid}")]
    public async Task<ActionResult<StoreSellerDetailRecord>> Get(Guid sellerId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await DetailAsync(db, sellerId, ct) is { } detail ? Ok(detail) : NotFound();
    }

    /// <summary>Records a payment made outside the site, claiming every unpaid earning up to the cutoff.</summary>
    [HttpPost("{sellerId:guid}/payments")]
    public async Task<ActionResult<StoreSellerDetailRecord>> RecordPayment(Guid sellerId, [FromBody] RecordSellerPaymentRequest request, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.AppUsers.AnyAsync(u => u.Id == sellerId, ct)) return NotFound();
        if (request.PaidOnUtc > Now.AddDays(1)) return BadRequest("A payment can't be dated in the future.");
        var (_, refusal) = await StoreSellerLedger.RecordPaymentAsync(db, sellerId, request.CutoffUtc, request.Expected, request.PaidOnUtc,
            request.Reference, me, Now, ct);
        if (refusal is not null) return Conflict(refusal);
        return Ok(await DetailAsync(db, sellerId, ct));
    }

    [HttpPost("{sellerId:guid}/payments/{payoutId:guid}/void")]
    public async Task<ActionResult<StoreSellerDetailRecord>> VoidPayment(Guid sellerId, Guid payoutId, [FromBody] VoidSellerPaymentRequest request, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreSellerPayouts.AnyAsync(p => p.Id == payoutId && p.SellerAppUserId == sellerId, ct)) return NotFound();
        if (await StoreSellerLedger.VoidAsync(db, payoutId, request.Reason, me, Now, ct) is { } refusal) return BadRequest(refusal);
        return Ok(await DetailAsync(db, sellerId, ct));
    }

    [HttpPost("{sellerId:guid}/adjustments")]
    public async Task<ActionResult<StoreSellerDetailRecord>> Adjust(Guid sellerId, [FromBody] SellerAdjustmentRequest request, CancellationToken ct)
    {
        var me = GetCurrentUserIdOrThrow();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.AppUsers.AnyAsync(u => u.Id == sellerId, ct)) return NotFound();
        if (await StoreSellerLedger.AdjustAsync(db, sellerId, request.Amount, request.Note, me, Now, ct) is { } refusal) return BadRequest(refusal);
        return Ok(await DetailAsync(db, sellerId, ct));
    }

    /// <summary>Every earning line, for a spreadsheet — the seller's year-end record, or the store's books.</summary>
    [HttpGet("{sellerId:guid}/export.csv")]
    public async Task<IActionResult> Export(Guid sellerId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.AppUsers.AnyAsync(u => u.Id == sellerId, ct)) return NotFound();
        var rows = await db.StoreSellerEarnings.AsNoTracking().Where(e => e.SellerAppUserId == sellerId).OrderBy(e => e.OccurredUtc).ToListAsync(ct);
        var numbers = await db.StoreOrders.AsNoTracking().Where(o => rows.Select(r => r.OrderId).Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);
        var payouts = await db.StoreSellerPayouts.AsNoTracking().Where(p => p.SellerAppUserId == sellerId).ToDictionaryAsync(p => p.Id, ct);
        var lines = new List<string> { StoreCsv.Line("Date", "Kind", "Order", "Units", "Amount", "Note", "PaidOn", "PaymentReference") };
        lines.AddRange(rows.Select(r => StoreCsv.Line(r.OccurredUtc.ToString("yyyy-MM-dd"), r.Kind, r.OrderId is { } o ? numbers.GetValueOrDefault(o) : null,
            r.Units, r.Amount, r.Note, r.PayoutId is { } p && payouts.TryGetValue(p, out var paid) ? paid.PaidOnUtc.ToString("yyyy-MM-dd") : null,
            r.PayoutId is { } q && payouts.TryGetValue(q, out var reference) ? reference.Reference : null)));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\n", lines))).ToArray(), "text/csv",
            $"seller-earnings-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private async Task<StoreSellerDetailRecord?> DetailAsync(BenDataContext db, Guid sellerId, CancellationToken ct)
    {
        var person = await db.AppUsers.AsNoTracking().Where(u => u.Id == sellerId).Select(u => new { u.DisplayName, u.Email }).FirstOrDefaultAsync(ct);
        if (person is null) return null;
        var settings = await StoreSettingsReader.ReadAsync(db, ct);
        var cutoff = StoreSellerLedger.DefaultCutoff(Now, settings.ReturnsWindowDays);
        return new StoreSellerDetailRecord(sellerId, person.DisplayName ?? person.Email ?? "A seller", person.Email,
            await StoreSellerLedger.SummaryAsync(db, sellerId, cutoff, ct), settings.ReturnsWindowDays);
    }
}
