using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// A group's own view of the money trail (item 168): their charges, payments and adjustments,
/// and a downloadable receipt for every payment — re-downloadable forever by the same number.
/// </summary>
/// <remarks>
/// <para>Gated like the subscription quote — <c>OrganizationSettings/Read</c> — because who may
/// see what the group is billed is exactly who may see its plan. Referral payouts never appear
/// here: they are between the platform and the referrer, not the group's business.</para>
/// <para>The receipt is generated from the frozen ledger row, so reprinting it in five years
/// produces the document that was true on the day — rate edits and renames notwithstanding.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/billing")]
public sealed class OrganizationBillingController : Cms.OrgCmsControllerBase
{
    public OrganizationBillingController(
        IDbContextFactory<BenDataContext> dbFactory,
        AutoMapper.IMapper mapper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    private IDbContextFactory<BenDataContext> _db => DbFactory;

    private async Task<bool> MayReadAsync(Guid organizationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        return userId is { } u && await IsCmsAuthorizedAsync(
            u, organizationId, OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Read, ct);
    }

    /// <summary>
    /// The caller's OWN overflow seat in this group (item 144), or null. Gated on being the
    /// holder, not on settings permission: a seat is a bill addressed to one person, and an
    /// ordinary member paying for their own seat must be able to see it without being able to
    /// read the group's billing.
    /// </summary>
    [HttpGet("my-seat")]
    public async Task<ActionResult<MyMemberSeatRecord?>> GetMySeat(Guid organizationId, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var seat = await db.MemberSeatSubscriptions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.AppUserId == userId)
            .Select(s => new MyMemberSeatRecord(
                s.OrganizationId, s.Organization.Name, s.Status, s.Interval,
                s.PriceAtStart, s.CurrentPeriodEnd))
            .FirstOrDefaultAsync(ct);
        return Ok(seat);
    }

    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<OrgBillingHistoryRecord>>> GetHistory(
        Guid organizationId, CancellationToken ct)
    {
        if (!await MayReadAsync(organizationId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var rows = await db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId)
            .OrderByDescending(e => e.DateCreated)
            .Select(e => new OrgBillingHistoryRecord(
                e.Id, e.Kind, e.Amount, e.AdjustmentIsCredit, e.TaxRatePercent, e.TaxAmount,
                e.Description, e.PaymentReference, e.ReceiptNumber,
                e.PeriodStart, e.PeriodEnd, e.DateCreated))
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>
    /// The receipt for one payment row, as a self-contained HTML document the browser can save
    /// or print. Only Payment rows have receipts — a charge is a bill, not proof of payment.
    /// </summary>
    [HttpGet("receipts/{entryId:guid}")]
    public async Task<IActionResult> GetReceipt(Guid organizationId, Guid entryId, CancellationToken ct)
    {
        if (!await MayReadAsync(organizationId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var e = await db.BillingLedgerEntries.AsNoTracking()
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == entryId && x.OrganizationId == organizationId, ct);
        if (e is null) return NotFound();
        if (e.Kind != BillingLedgerKind.Payment || e.ReceiptNumber is null)
            return BadRequest("Only payments have receipts.");

        // The group's billing details: nominated billing contact (first, by nomination date),
        // and the address whose state set the rate.
        var contact = await db.OrganizationBillingContacts.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.DateCreated)
            .Select(c => c.AppUser.DisplayName ?? c.AppUser.Email)
            .FirstOrDefaultAsync(ct);
        var address = await db.OrganizationAddresses.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId)
            .OrderBy(a => a.DateCreated)
            .Select(a => new { a.StreetAddress1, a.City, a.State, a.ZipCode })
            .FirstOrDefaultAsync(ct);

        var total = e.Amount + e.TaxAmount;
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html><html><head><meta charset="utf-8">
            <title>Receipt R-{{e.ReceiptNumber:00000}}</title>
            <style>
              body { font-family: Georgia, serif; max-width: 40rem; margin: 3rem auto; color: #222; }
              h1 { font-size: 1.4rem; border-bottom: 2px solid #222; padding-bottom: .5rem; }
              table { width: 100%; border-collapse: collapse; margin-top: 1.5rem; }
              td, th { padding: .4rem .6rem; text-align: left; border-bottom: 1px solid #ddd; }
              td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
              tr.total td { font-weight: bold; border-top: 2px solid #222; }
              .meta { color: #555; font-size: .9rem; }
            </style></head><body>
            <h1>IsHaunted.com — Receipt R-{{e.ReceiptNumber:00000}}</h1>
            <p class="meta">
              Date: {{e.DateCreated:MM/dd/yyyy}}<br>
              Billed to: {{System.Net.WebUtility.HtmlEncode(e.Organization!.Name)}}{{(contact is null ? "" : $"<br>Billing contact: {System.Net.WebUtility.HtmlEncode(contact)}")}}
            """);
        if (address is not null)
        {
            html.Append($"<br>{System.Net.WebUtility.HtmlEncode($"{address.StreetAddress1}, {address.City}, {address.State} {address.ZipCode}")}");
        }
        if (!string.IsNullOrWhiteSpace(e.PaymentReference))
        {
            html.Append($"<br>Payment reference: {System.Net.WebUtility.HtmlEncode(e.PaymentReference)}");
        }
        html.Append($$"""
            </p>
            <table>
              <tr><th>Description</th><th class="num">Amount</th></tr>
              <tr><td>{{System.Net.WebUtility.HtmlEncode(e.Description)}}{{(e.PeriodStart is null ? "" : $" ({e.PeriodStart:MM/dd/yyyy} – {e.PeriodEnd:MM/dd/yyyy})")}}</td>
                  <td class="num">${{e.Amount:0.00}}</td></tr>
              <tr><td>Tax ({{e.TaxRatePercent:0.##}}%)</td><td class="num">${{e.TaxAmount:0.00}}</td></tr>
              <tr class="total"><td>Total paid</td><td class="num">${{total:0.00}}</td></tr>
            </table>
            <p class="meta">This receipt was generated from the payment record and can be
            re-downloaded at any time from your group's billing history.</p>
            </body></html>
            """);

        return File(Encoding.UTF8.GetBytes(html.ToString()), "text/html",
            $"receipt-R-{e.ReceiptNumber:00000}.html");
    }
}
