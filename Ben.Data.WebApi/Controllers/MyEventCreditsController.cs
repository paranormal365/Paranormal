using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Event credits a person holds in their own right, not through a group (10/21/2026).
/// </summary>
/// <remarks>
/// <para><b>Ben's rule:</b> "Events may be purchased by anyone." The event organizer is whoever bought
/// the credit and applied it to the event, so a person who organizes events for a group they help run
/// can buy the credits themselves, and publishing one of that group's events spends theirs when the
/// group holds none.</para>
///
/// <para>Scoped to the bearer token's own account throughout, like every <c>api/me</c> surface. A person
/// is not a group and has no plan, so they always pay the full price.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/event-credits")]
public sealed class MyEventCreditsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IStripeGateway _stripe;
    private readonly IConfiguration _configuration;

    public MyEventCreditsController(
        IDbContextFactory<BenDataContext> db, IStripeGateway stripe, IConfiguration configuration)
    {
        _db = db;
        _stripe = stripe;
        _configuration = configuration;
    }

    /// <summary>The credits this person holds, spent ones included, and what one costs them today.</summary>
    [HttpGet]
    public async Task<ActionResult<OrgEventCreditsView>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await EventCreditViews.BuildAsync(db, organizationId: null, userId, DateTime.UtcNow, ct));
    }

    /// <summary>Opens a Stripe checkout for credits this person will hold.</summary>
    [HttpPost("checkout")]
    public async Task<ActionResult<StartCheckoutResponse>> Checkout(
        [FromQuery] int quantity, [FromQuery] int? bundle, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var me = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.DisplayName, u.Email })
            .FirstOrDefaultAsync(ct);
        if (me is null) return Unauthorized();

        var baseUrl = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        var outcome = await EventCreditCheckout.StartAsync(
            db, _stripe, organizationId: null, me.DisplayName ?? me.Email ?? "IsHaunted member",
            existingCustomerRef: null, userId, quantity, bundle,
            returnUrl: $"{baseUrl}/my-event-credits", ct);

        return outcome switch
        {
            { Response: { } ok } => Ok(ok),
            { Refusal: { } why } => BadRequest(why),
            _ => Problem(outcome.Unavailable, statusCode: 503),
        };
    }

    /// <summary>This person's own payments for credits, newest first, each with its receipt number.</summary>
    [HttpGet("history")]
    public async Task<ActionResult<IEnumerable<OrgBillingHistoryRecord>>> History(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.OrganizationId == null && e.PayerAppUserId == userId)
            .OrderByDescending(e => e.DateCreated)
            .Select(e => new OrgBillingHistoryRecord(
                e.Id, e.Kind, e.Amount, e.AdjustmentIsCredit, e.TaxRatePercent, e.TaxAmount,
                e.Description, e.PaymentReference, e.ReceiptNumber,
                e.PeriodStart, e.PeriodEnd, e.DateCreated))
            .ToListAsync(ct));
    }

    /// <summary>The receipt for one of this person's own payments.</summary>
    [HttpGet("receipts/{entryId:guid}")]
    public async Task<IActionResult> Receipt(Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        // Only their own: a row that is not theirs is answered as missing, so receipts cannot be
        // fished for by id.
        var e = await db.BillingLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == entryId && x.OrganizationId == null && x.PayerAppUserId == userId, ct);
        if (e is null) return NotFound();
        if (e.Kind != BillingLedgerKind.Payment || e.ReceiptNumber is null)
            return BadRequest("Only payments have receipts.");

        var name = await db.AppUsers.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.DisplayName ?? u.Email).FirstOrDefaultAsync(ct) ?? "IsHaunted member";

        var html = OrganizationBillingController.ReceiptHtml(e, name, contact: null, addressLine: null,
            "your event credits page");
        return File(Encoding.UTF8.GetBytes(html), "text/html", $"receipt-R-{e.ReceiptNumber:00000}.html");
    }
}
