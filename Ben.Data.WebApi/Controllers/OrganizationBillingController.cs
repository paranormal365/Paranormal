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
    /// The caller's OWN overflow seat in this group (item 144) — a list of at most one. Gated on
    /// being the holder, not on settings permission: a seat is a bill addressed to one person,
    /// and an ordinary member paying for their own seat must be able to see it without being
    /// able to read the group's billing.
    /// </summary>
    /// <remarks>
    /// A LIST rather than a nullable single, deliberately: <c>Ok(null)</c> answers 204, which a
    /// client cannot tell apart from a refusal or a server error, and this is somebody's BILL —
    /// silently showing nothing is the worst of the three. The list shape carries the difference
    /// through <c>LoadResult</c> all the way to the page (item 178's smaller note).
    /// </remarks>
    [HttpGet("my-seats")]
    public async Task<ActionResult<IEnumerable<MyMemberSeatRecord>>> GetMySeats(
        Guid organizationId, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await db.MemberSeatSubscriptions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId && s.AppUserId == userId)
            .Select(s => new MyMemberSeatRecord(
                s.OrganizationId, s.Organization.Name, s.Status, s.Interval,
                s.PriceAtStart, s.CurrentPeriodEnd))
            .ToListAsync(ct));
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
    /// The event credits this group holds, and what one costs today (item 235, phase 1B.5).
    /// </summary>
    /// <remarks>
    /// <para>Gated like the rest of this controller: who may see what the group is billed is who
    /// may see what it has bought. A credit is $99 of the group's money sitting unspent, so it
    /// belongs on the same page as the plan and the receipts rather than hidden behind the events
    /// section — somebody looking for "what have we paid for?" looks here.</para>
    ///
    /// <para><b>Spent credits stay in the list.</b> The question a year later is not "how many do
    /// we have" but "did we pay for that weekend in October", and a list that quietly drops the
    /// answer as soon as it is used cannot be asked.</para>
    /// </remarks>
    [HttpGet("event-credits")]
    public async Task<ActionResult<OrgEventCreditsView>> GetEventCredits(
        Guid organizationId, CancellationToken ct)
    {
        if (!await MayReadAsync(organizationId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);

        // Priced the way checkout prices it: the paid-subscriber percentage for a group with a
        // standing paid plan, and the bundles from the settings (10/21/2026).
        return Ok(await Services.Events.EventCreditViews.BuildAsync(
            db, organizationId, appUserId: null, DateTime.UtcNow, ct));
    }

    // ── Ghost Walk Tour subscription (10/21/2026) ─────────────────────────────

    /// <summary>
    /// The group's Ghost Walk Tour subscription and today's price. Read with the billing key.
    /// </summary>
    [HttpGet("ghost-walk")]
    public async Task<ActionResult<Ben.Service.Models.GhostWalkSubscriptionView>> GetGhostWalk(
        Guid organizationId, CancellationToken ct)
    {
        if (!await MayReadAsync(organizationId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await GhostWalkViewAsync(db, organizationId, ct));
    }

    /// <summary>Stops the subscription renewing. The month already paid for still runs out.</summary>
    [HttpPost("ghost-walk/cancel")]
    public Task<ActionResult<Ben.Service.Models.GhostWalkSubscriptionView>> CancelGhostWalk(
        Guid organizationId, CancellationToken ct)
        => SetGhostWalkRenewalAsync(organizationId, cancel: true, ct);

    /// <summary>Takes back a cancellation before the paid month ends.</summary>
    [HttpPost("ghost-walk/resume")]
    public Task<ActionResult<Ben.Service.Models.GhostWalkSubscriptionView>> ResumeGhostWalk(
        Guid organizationId, CancellationToken ct)
        => SetGhostWalkRenewalAsync(organizationId, cancel: false, ct);

    private async Task<ActionResult<Ben.Service.Models.GhostWalkSubscriptionView>> SetGhostWalkRenewalAsync(
        Guid organizationId, bool cancel, CancellationToken ct)
    {
        if (GetCurrentUserId() is not { } userId) return Unauthorized();
        // Changing what the group will be charged is an act on its money: the settings key to change.
        if (!await IsCmsAuthorizedAsync(userId, organizationId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var sub = await db.GhostWalkSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);
        var now = DateTime.UtcNow;
        if (sub is null || !sub.IsStanding(now))
            return BadRequest("This group has no Ghost Walk Tour subscription running, so there is nothing to change.");

        sub.CancelAtPeriodEnd = cancel;
        sub.DateUpdated = now;
        sub.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(await GhostWalkViewAsync(db, organizationId, ct));
    }

    internal static async Task<Ben.Service.Models.GhostWalkSubscriptionView> GhostWalkViewAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        var offer = await Services.Tours.GhostWalkOffer.ReadAsync(db, ct);
        var sub = await db.GhostWalkSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);
        var now = DateTime.UtcNow;
        return new Ben.Service.Models.GhostWalkSubscriptionView(
            offer.OnSale, offer.MonthlyPrice, sub?.Status, sub?.IsStanding(now) == true,
            sub?.CurrentPeriodEnd, sub?.CancelAtPeriodEnd == true, sub?.PriceAtPeriodStart);
    }

    /// <summary>
    /// The receipt for one payment row, as a self-contained HTML document the browser can save
    /// or print. Only Payment rows have receipts — a charge is a bill, not proof of payment.
    /// </summary>
    [HttpGet("receipts/{entryId:guid}")]
    public async Task<IActionResult> GetReceipt(Guid organizationId, Guid entryId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var e = await db.BillingLedgerEntries.AsNoTracking()
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == entryId && x.OrganizationId == organizationId, ct);
        if (e is null) return NotFound();

        // Settings permission, OR being the person who paid: a member's seat payment lands on
        // the group's ledger, and the one who handed over the card must be able to reprint
        // their own receipt without holding the group's keys.
        if (!await MayReadAsync(organizationId, ct)
            && e.CreatedByAppUserId != GetCurrentUserId())
            return Forbid();
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

        var html = ReceiptHtml(e, e.Organization!.Name, contact,
            address is null ? null : $"{address.StreetAddress1}, {address.City}, {address.State} {address.ZipCode}",
            "your group's billing history");

        return File(Encoding.UTF8.GetBytes(html), "text/html",
            $"receipt-R-{e.ReceiptNumber:00000}.html");
    }

    /// <summary>
    /// A receipt as a self-contained HTML document, from the frozen ledger row. Shared by the group's
    /// billing page and a person's own event-credit page, so the two can never print it differently.
    /// </summary>
    internal static string ReceiptHtml(
        Ben.Data.Source.Entities.BillingLedgerEntry e, string billedTo, string? contact, string? addressLine,
        string whereKept)
    {
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
              Billed to: {{System.Net.WebUtility.HtmlEncode(billedTo)}}{{(contact is null ? "" : $"<br>Billing contact: {System.Net.WebUtility.HtmlEncode(contact)}")}}
            """);
        if (addressLine is not null)
        {
            html.Append($"<br>{System.Net.WebUtility.HtmlEncode(addressLine)}");
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
            re-downloaded at any time from {{whereKept}}.</p>
            </body></html>
            """);

        return html.ToString();
    }
}
