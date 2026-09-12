using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The money trail (item 168): an append-only ledger of charges, payments, adjustments and
/// referral payouts, plus the tax-rate rules and the referral standings.
/// </summary>
/// <remarks>
/// <para><b>Nothing here edits or deletes a ledger row.</b> There is deliberately no PUT and no
/// DELETE: a wrong entry is answered by an Adjustment naming the mistake, the way a paper ledger
/// works, and "who changed this number?" therefore has exactly one possible answer — nobody.</para>
/// <para><b>Tax is computed here, not sent by the caller.</b> The admin records the pre-tax
/// amount; the group's state resolves the rate, and both the rate and the dollars are frozen on
/// the row. An admin who could type the tax directly could also mistype it.</para>
/// <para><b>Receipt numbers are sequential and database-guarded.</b> The unique index decides a
/// race; the loser retries with the next number.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/billing")]
public sealed class AdminBillingController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminBillingController(IDbContextFactory<BenDataContext> db) => _db = db;

    // ── The ledger ────────────────────────────────────────────────────────────

    [HttpGet("ledger")]
    public async Task<ActionResult<IEnumerable<BillingLedgerEntryRecord>>> GetLedger(
        [FromQuery] Guid? orgId, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.BillingLedgerEntries.AsNoTracking();
        if (orgId is { } o) query = query.Where(e => e.OrganizationId == o);

        var rows = await query
            .OrderByDescending(e => e.DateCreated)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(e => new BillingLedgerEntryRecord(
                e.Id, e.Kind, e.OrganizationId,
                e.Organization != null ? e.Organization.Name : null,
                e.ReferrerAppUserId,
                e.ReferrerAppUser != null ? (e.ReferrerAppUser.DisplayName ?? e.ReferrerAppUser.Email) : null,
                e.Amount, e.AdjustmentIsCredit, e.TaxRatePercent, e.TaxAmount,
                e.Description, e.PaymentReference, e.ReceiptNumber,
                e.PeriodStart, e.PeriodEnd, e.DateCreated,
                e.CreatedByAppUser.DisplayName ?? e.CreatedByAppUser.Email ?? "?"))
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpPost("organizations/{orgId:guid}/charges")]
    public Task<ActionResult<BillingLedgerEntryRecord>> RecordCharge(
        Guid orgId, [FromBody] RecordBillingEntryRequest request, CancellationToken ct)
        => RecordOrgEntryAsync(orgId, BillingLedgerKind.Charge, request, ct);

    [HttpPost("organizations/{orgId:guid}/payments")]
    public Task<ActionResult<BillingLedgerEntryRecord>> RecordPayment(
        Guid orgId, [FromBody] RecordBillingEntryRequest request, CancellationToken ct)
        => RecordOrgEntryAsync(orgId, BillingLedgerKind.Payment, request, ct);

    private async Task<ActionResult<BillingLedgerEntryRecord>> RecordOrgEntryAsync(
        Guid orgId, BillingLedgerKind kind, RecordBillingEntryRequest request, CancellationToken ct)
    {
        // Zero is ALLOWED here, and only here (adjustments and payouts still demand a positive
        // number). A 100%-off trial period costs nothing and still has to appear in the ledger:
        // the row is how anyone later answers "what happened in September?" — and its description
        // names the coupon that made it free. Refusing it left a three-month hole in the billing
        // history of exactly the groups Ben is courting first (item 195, found 2026-08-26 while
        // proving the trial before it goes on sale). Negative stays refused: a credit is an
        // Adjustment with the credit flag, not a charge with a minus sign.
        if (request.Amount < 0) return BadRequest("The amount cannot be negative; a credit is an adjustment.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("A ledger row needs a description — an unexplained number is unanswerable later.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct)) return NotFound("Organization not found.");

        // Charges tax what will be owed; a payment taxes nothing itself — its tax was on the
        // charge it settles. Recording it separately would count the same tax twice.
        decimal ratePercent = 0m, tax = 0m;
        if (kind == BillingLedgerKind.Charge)
        {
            (_, ratePercent) = await TaxResolver.ForOrganizationAsync(db, orgId, ct);
            tax = TaxResolver.TaxOn(request.Amount, ratePercent);
        }

        var entry = new BillingLedgerEntry
        {
            Id                 = Guid.NewGuid(),
            Kind               = kind,
            OrganizationId     = orgId,
            Amount             = decimal.Round(request.Amount, 2),
            TaxRatePercent     = ratePercent,
            TaxAmount          = tax,
            Description        = request.Description.Trim(),
            PaymentReference   = request.PaymentReference?.Trim(),
            PeriodStart        = request.PeriodStart,
            PeriodEnd          = request.PeriodEnd,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = GetCurrentUserId(),
        };

        if (kind == BillingLedgerKind.Payment)
            return await SaveWithReceiptNumberAsync(db, entry, ct);

        db.BillingLedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(await ReadBackAsync(db, entry.Id, ct));
    }

    /// <summary>
    /// Payments get the next receipt number. Max+1 races under concurrency, so the unique index
    /// is the referee: the loser's save throws, and it retries with a fresh number. Three
    /// attempts is far beyond what a manual-entry admin screen can actually collide on.
    /// </summary>
    private async Task<ActionResult<BillingLedgerEntryRecord>> SaveWithReceiptNumberAsync(
        BenDataContext db, BillingLedgerEntry entry, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            entry.ReceiptNumber = 1 + await db.BillingLedgerEntries
                .MaxAsync(e => (int?)e.ReceiptNumber, ct) ?? 1;
            db.BillingLedgerEntries.Add(entry);
            try
            {
                await db.SaveChangesAsync(ct);
                return Ok(await ReadBackAsync(db, entry.Id, ct));
            }
            catch (DbUpdateException)
            {
                db.Entry(entry).State = EntityState.Detached;
            }
        }
        return Problem("Could not assign a receipt number — try again.", statusCode: 503);
    }

    [HttpPost("organizations/{orgId:guid}/adjustments")]
    public async Task<ActionResult<BillingLedgerEntryRecord>> RecordAdjustment(
        Guid orgId, [FromBody] RecordAdjustmentRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("The amount must be positive; direction is the credit flag.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("An adjustment IS its explanation — the description is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct)) return NotFound("Organization not found.");

        var entry = new BillingLedgerEntry
        {
            Id                 = Guid.NewGuid(),
            Kind               = BillingLedgerKind.Adjustment,
            OrganizationId     = orgId,
            Amount             = decimal.Round(request.Amount, 2),
            AdjustmentIsCredit = request.IsCredit,
            Description        = request.Description.Trim(),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = GetCurrentUserId(),
        };
        db.BillingLedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(await ReadBackAsync(db, entry.Id, ct));
    }

    private static async Task<BillingLedgerEntryRecord> ReadBackAsync(
        BenDataContext db, Guid id, CancellationToken ct)
        => await db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new BillingLedgerEntryRecord(
                e.Id, e.Kind, e.OrganizationId,
                e.Organization != null ? e.Organization.Name : null,
                e.ReferrerAppUserId,
                e.ReferrerAppUser != null ? (e.ReferrerAppUser.DisplayName ?? e.ReferrerAppUser.Email) : null,
                e.Amount, e.AdjustmentIsCredit, e.TaxRatePercent, e.TaxAmount,
                e.Description, e.PaymentReference, e.ReceiptNumber,
                e.PeriodStart, e.PeriodEnd, e.DateCreated,
                e.CreatedByAppUser.DisplayName ?? e.CreatedByAppUser.Email ?? "?"))
            .SingleAsync(ct);

    // ── Event credits (item 235) ──────────────────────────────────────────────

    /// <summary>
    /// Every event credit ever sold, newest first.
    /// </summary>
    /// <remarks>
    /// The SuperAdmin view exists because <c>RefundedUtc</c> would otherwise be a column only SQL
    /// could write — a rule the code keeps and no screen can reach, which is the shape of a
    /// half-built feature rather than a decision.
    /// </remarks>
    [HttpGet("event-credits")]
    public async Task<ActionResult<IEnumerable<AdminEventCreditRecord>>> GetEventCredits(
        [FromQuery] Guid? orgId, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.EventCredits.AsNoTracking();
        if (orgId is { } o) query = query.Where(c => c.OwnerOrganizationId == o);

        var rows = await query
            .OrderByDescending(c => c.PurchasedUtc)
            .Take(Math.Clamp(take, 1, 1000))
            .Select(c => new AdminEventCreditRecord(
                c.Id,
                c.OwnerOrganizationId,
                c.OwnerOrganization != null
                    ? c.OwnerOrganization.Name
                    : (c.OwnerAppUser != null ? (c.OwnerAppUser.DisplayName ?? c.OwnerAppUser.Email ?? "?") : "?"),
                c.CreatedByAppUser.DisplayName ?? c.CreatedByAppUser.Email ?? "?",
                c.PriceAtPurchase, c.Currency, c.PurchasedUtc, c.ExpiresUtc, c.ExpiryWarningSentUtc,
                c.SpentUtc, c.SpentOnHostedEventId,
                c.SpentOnHostedEvent != null ? c.SpentOnHostedEvent.Name : null,
                c.RefundedUtc,
                c.RefundedByAppUserId != null
                    ? db.Users.Where(u => u.Id == c.RefundedByAppUserId)
                        .Select(u => u.DisplayName ?? u.Email).FirstOrDefault()
                    : null,
                c.RefundedReason,
                c.ReceiptNumber,
                c.GrantedReason))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Hands a group credits nobody paid for.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> Until it did, the only way a credit could reach a group was a
    /// live card payment. That left support with no remedy for the one case that actually turns
    /// up — somebody paid and it did not land — and it left the refund beside this one impossible
    /// to exercise on any deployment, because there was never a credit to refund.</para>
    ///
    /// <para><b>It is not a sale, and nothing reaches the ledger.</b> A $0 charge and payment pair
    /// would put a sale that never happened into the money trail, and a receipt would say somebody
    /// paid nothing. <c>PriceAtPurchase</c> is zero, there is no provider reference and no receipt
    /// number; the reason and the granting admin are the record, which is why the reason is
    /// required rather than optional.</para>
    ///
    /// <para><b>In every other respect it is an ordinary credit</b> — a year to use it, spent at
    /// publish, oldest first, warned at thirty days, refundable while unspent. A granted credit
    /// that behaved differently would be a second product wearing the first one's name.</para>
    /// </remarks>
    [HttpPost("organizations/{orgId:guid}/event-credits")]
    public async Task<ActionResult<IEnumerable<AdminEventCreditRecord>>> GrantEventCredits(
        Guid orgId, [FromBody] GrantEventCreditsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("A granted credit needs a reason — it is the only thing separating "
                            + "a support fix from a giveaway, and the only record that it happened.");

        var count = Math.Clamp(request.Quantity <= 0 ? 1 : request.Quantity,
                               1, Services.Events.EventCredits.MaximumPerPurchase);

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct)) return NotFound("Organization not found.");

        var now = DateTime.UtcNow;
        var granted = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var credit = new EventCredit
            {
                Id                  = Guid.NewGuid(),
                OwnerOrganizationId = orgId,
                PriceAtPurchase     = 0m,
                Currency            = "USD",
                PurchasedUtc        = now,
                ExpiresUtc          = now.Add(Services.Events.EventCredits.Life),
                GrantedReason       = request.Reason.Trim(),
                DateCreated         = now,
                CreatedByAppUserId  = GetCurrentUserId(),
            };
            db.EventCredits.Add(credit);
            granted.Add(credit.Id);
        }

        await db.SaveChangesAsync(ct);

        var rows = new List<AdminEventCreditRecord>();
        foreach (var id in granted) rows.Add(await ReadCreditBackAsync(db, id, ct));
        return Ok(rows);
    }

    /// <summary>
    /// Refunds one credit: it can never be spent again, and the ledger says why.
    /// </summary>
    /// <remarks>
    /// <para><b>A spent credit is refused</b>, and the refusal names the event. Ben's rule is one
    /// event, one credit, for the life of that event — a live event whose credit was handed back
    /// would make its own payment record untrue, and re-publishing it would then never charge
    /// again. When money genuinely has to go back on a live event, the honest instrument is a
    /// credit adjustment on the group's ledger, which this screen already offers.</para>
    ///
    /// <para><b>An expired credit may still be refunded.</b> That is exactly the goodwill case —
    /// somebody who paid and never got to use it — and marking the row keeps the decision beside
    /// the money rather than in an email.</para>
    ///
    /// <para><b>The adjustment is written here by default</b>, because a refund nobody recorded is
    /// a hole in the money trail. It can be turned off for a refund put through Stripe, whose own
    /// row reaches the ledger by another route and would otherwise be counted twice.</para>
    /// </remarks>
    [HttpPost("event-credits/{creditId:guid}/refund")]
    public async Task<ActionResult<AdminEventCreditRecord>> RefundEventCredit(
        Guid creditId, [FromBody] RefundEventCreditRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("A refund needs a reason — a row that does not say why is one nobody can answer for later.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var credit = await db.EventCredits
            .Include(c => c.SpentOnHostedEvent)
            .FirstOrDefaultAsync(c => c.Id == creditId, ct);
        if (credit is null) return NotFound("That credit no longer exists.");

        if (credit.RefundedUtc is not null)
            return BadRequest($"That credit was already refunded on {credit.RefundedUtc:MM/dd/yyyy}.");

        if (credit.SpentUtc is not null)
            return BadRequest(
                $"That credit was spent on \"{credit.SpentOnHostedEvent?.Name ?? "an event"}\" and cannot be "
              + "handed back — one event, one credit, for the life of that event. To give the money "
              + "back, record a credit adjustment on the group's ledger instead.");

        var now = DateTime.UtcNow;
        credit.RefundedUtc         = now;
        credit.RefundedByAppUserId = GetCurrentUserId();
        credit.RefundedReason      = request.Reason.Trim();
        credit.DateUpdated         = now;
        credit.UpdatedByAppUserId  = GetCurrentUserId();

        // A granted credit cost nothing, so there is nothing to hand back and a $0 adjustment
        // would only add a line saying so. Revoking one is what refunding it means.
        if (request.RecordAdjustment && credit.PriceAtPurchase > 0m
            && credit.OwnerOrganizationId is { } orgId)
            db.BillingLedgerEntries.Add(new BillingLedgerEntry
            {
                Id                 = Guid.NewGuid(),
                Kind               = BillingLedgerKind.Adjustment,
                OrganizationId     = orgId,
                Amount             = credit.PriceAtPurchase,
                AdjustmentIsCredit = true,
                Description        = $"Event credit refunded — {credit.RefundedReason}",
                DateCreated        = now,
                CreatedByAppUserId = GetCurrentUserId(),
            });

        await db.SaveChangesAsync(ct);

        return Ok(await ReadCreditBackAsync(db, creditId, ct));
    }

    private static Task<AdminEventCreditRecord> ReadCreditBackAsync(
        BenDataContext db, Guid creditId, CancellationToken ct)
        => db.EventCredits.AsNoTracking()
            .Where(c => c.Id == creditId)
            .Select(c => new AdminEventCreditRecord(
                c.Id,
                c.OwnerOrganizationId,
                c.OwnerOrganization != null
                    ? c.OwnerOrganization.Name
                    : (c.OwnerAppUser != null ? (c.OwnerAppUser.DisplayName ?? c.OwnerAppUser.Email ?? "?") : "?"),
                c.CreatedByAppUser.DisplayName ?? c.CreatedByAppUser.Email ?? "?",
                c.PriceAtPurchase, c.Currency, c.PurchasedUtc, c.ExpiresUtc, c.ExpiryWarningSentUtc,
                c.SpentUtc, c.SpentOnHostedEventId,
                c.SpentOnHostedEvent != null ? c.SpentOnHostedEvent.Name : null,
                c.RefundedUtc,
                c.RefundedByAppUserId != null
                    ? db.Users.Where(u => u.Id == c.RefundedByAppUserId)
                        .Select(u => u.DisplayName ?? u.Email).FirstOrDefault()
                    : null,
                c.RefundedReason,
                c.ReceiptNumber,
                c.GrantedReason))
            .SingleAsync(ct);

    // ── Tax rates ─────────────────────────────────────────────────────────────

    [HttpGet("tax-rates")]
    public async Task<ActionResult<IEnumerable<TaxRateRuleRecord>>> GetTaxRates(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return Ok(await db.TaxRateRules.AsNoTracking()
            .OrderBy(r => r.State)
            .Select(r => new TaxRateRuleRecord(r.Id, r.State, r.RatePercent, r.Notes, r.DateCreated, r.DateUpdated))
            .ToListAsync(ct));
    }

    /// <summary>Upsert by state — one rule per state is the invariant, so "create or update"
    /// is one intention, not two endpoints.</summary>
    [HttpPut("tax-rates")]
    public async Task<ActionResult<TaxRateRuleRecord>> SaveTaxRate(
        [FromBody] SaveTaxRateRuleRequest request, CancellationToken ct)
    {
        var state = request.State?.Trim().ToUpperInvariant() ?? "";
        if (state.Length != 2 || !state.All(char.IsAsciiLetterUpper))
            return BadRequest("State must be a two-letter code.");
        if (request.RatePercent is < 0 or > 25)
            return BadRequest("The rate must be between 0 and 25 percent.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var rule = await db.TaxRateRules.FirstOrDefaultAsync(r => r.State == state, ct);
        if (rule is null)
        {
            rule = new TaxRateRule
            {
                Id = Guid.NewGuid(), State = state,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = GetCurrentUserId(),
            };
            db.TaxRateRules.Add(rule);
        }
        else
        {
            rule.DateUpdated = DateTime.UtcNow;
            rule.UpdatedByAppUserId = GetCurrentUserId();
        }
        rule.RatePercent = decimal.Round(request.RatePercent, 2);
        rule.Notes = request.Notes?.Trim();
        await db.SaveChangesAsync(ct);
        return Ok(new TaxRateRuleRecord(rule.Id, rule.State, rule.RatePercent, rule.Notes, rule.DateCreated, rule.DateUpdated));
    }

    [HttpDelete("tax-rates/{id:guid}")]
    public async Task<IActionResult> DeleteTaxRate(Guid id, CancellationToken ct)
    {
        // Deleting a RULE is fine — unlike a ledger row, it is current configuration, and every
        // document that used it froze its own copy.
        await using var db = await _db.CreateDbContextAsync(ct);
        var rule = await db.TaxRateRules.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule is null) return NotFound();
        db.TaxRateRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Referrals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Every referrer's standing: coupons, redemptions, revenue attributed (what redeeming
    /// groups actually paid), discount given, and what has been paid out. What is OWED is not
    /// computed — the reward rule is a product decision not yet made — the two sides are shown
    /// so a human can settle it.
    /// </summary>
    [HttpGet("referrers")]
    public async Task<ActionResult<IEnumerable<ReferrerSummaryRecord>>> GetReferrers(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var referred = await db.Coupons.AsNoTracking()
            .Where(c => c.ReferrerAppUserId != null)
            .Select(c => new
            {
                ReferrerId = c.ReferrerAppUserId!.Value,
                Name = c.ReferrerAppUser!.DisplayName ?? c.ReferrerAppUser.Email ?? "?",
                Redemptions = c.Redemptions.Count,
                Revenue = c.Redemptions.Sum(r => (decimal?)r.Payable) ?? 0m,
                Discount = c.Redemptions.Sum(r => (decimal?)r.Discount) ?? 0m,
                CommissionPercent = c.ReferralCommissionPercent,
            })
            .ToListAsync(ct);

        var payouts = await db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.Kind == BillingLedgerKind.ReferralPayout && e.ReferrerAppUserId != null)
            .GroupBy(e => e.ReferrerAppUserId!.Value)
            .Select(g => new { ReferrerId = g.Key, Paid = g.Sum(e => e.Amount) })
            .ToDictionaryAsync(x => x.ReferrerId, x => x.Paid, ct);

        var rows = referred
            .GroupBy(x => new { x.ReferrerId, x.Name })
            .Select(g => new ReferrerSummaryRecord(
                g.Key.ReferrerId, g.Key.Name,
                g.Count(), g.Sum(x => x.Redemptions),
                g.Sum(x => x.Revenue), g.Sum(x => x.Discount),
                payouts.GetValueOrDefault(g.Key.ReferrerId),
                // Percent of revenue, per campaign (Ben's rule): each campaign's cut on what
                // its redeemers actually paid, rounded per campaign the way it would be settled.
                g.Sum(x => x.CommissionPercent is { } pct
                    ? Math.Round(x.Revenue * pct / 100m, 2, MidpointRounding.AwayFromZero) : 0m),
                g.All(x => x.CommissionPercent is not null)))
            .OrderByDescending(r => r.RevenueAttributed)
            .ToList();

        // A referrer who has been paid but whose coupons were later deactivated must still
        // appear — money went out; the standing cannot vanish with the coupon.
        foreach (var (referrerId, paid) in payouts.Where(p => rows.All(r => r.ReferrerAppUserId != p.Key)))
        {
            var name = await db.Users.AsNoTracking()
                .Where(u => u.Id == referrerId)
                .Select(u => u.DisplayName ?? u.Email ?? "?")
                .FirstOrDefaultAsync(ct) ?? "?";
            rows.Add(new ReferrerSummaryRecord(referrerId, name, 0, 0, 0m, 0m, paid));
        }

        return Ok(rows);
    }

    // ── Overflow seats (item 144) ────────────────────────────────────────────

    /// <summary>
    /// Every overflow seat, newest first — the manual billing worklist. Pending ones are the
    /// people to invoice; active ones are the people already paying for themselves.
    /// </summary>
    [HttpGet("member-seats")]
    public async Task<ActionResult<IEnumerable<MemberSeatAdminRecord>>> GetMemberSeats(
        [FromQuery] Guid? orgId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.MemberSeatSubscriptions.AsNoTracking();
        if (orgId is { } o) query = query.Where(s => s.OrganizationId == o);

        return Ok(await query
            .OrderByDescending(s => s.DateCreated)
            .Select(s => new MemberSeatAdminRecord(
                s.Id, s.OrganizationId, s.Organization.Name, s.AppUserId,
                s.AppUser.DisplayName ?? s.AppUser.Email ?? "?",
                s.Status, s.Interval, s.PriceAtStart,
                s.CurrentPeriodStart, s.CurrentPeriodEnd, s.DateCreated))
            .ToListAsync(ct));
    }

    /// <summary>
    /// Sets a seat's standing — the manual payment provider again, seat-sized. Activating one is
    /// what a recorded payment means; the payment itself is a ledger row, recorded separately so
    /// the money and the entitlement never disagree by being the same write.
    /// </summary>
    [HttpPut("member-seats/{seatId:guid}")]
    public async Task<ActionResult<MemberSeatAdminRecord>> SetMemberSeat(
        Guid seatId, [FromBody] SetMemberSeatRequest request, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var seat = await db.MemberSeatSubscriptions
            .Include(s => s.Organization).Include(s => s.AppUser)
            .FirstOrDefaultAsync(s => s.Id == seatId, ct);
        if (seat is null) return NotFound();

        if (request.Status == SubscriptionStatus.Active
            && (request.CurrentPeriodStart is null || request.CurrentPeriodEnd is null))
            return BadRequest("An active seat needs a period — start and end.");
        if (request.CurrentPeriodEnd is { } end && request.CurrentPeriodStart is { } start && end <= start)
            return BadRequest("The period ends before it starts.");

        seat.Status             = request.Status;
        seat.CurrentPeriodStart = request.CurrentPeriodStart;
        seat.CurrentPeriodEnd   = request.CurrentPeriodEnd;
        seat.DateUpdated        = DateTime.UtcNow;
        seat.UpdatedByAppUserId = GetCurrentUserId();
        await db.SaveChangesAsync(ct);

        return Ok(new MemberSeatAdminRecord(
            seat.Id, seat.OrganizationId, seat.Organization.Name, seat.AppUserId,
            seat.AppUser.DisplayName ?? seat.AppUser.Email ?? "?",
            seat.Status, seat.Interval, seat.PriceAtStart,
            seat.CurrentPeriodStart, seat.CurrentPeriodEnd, seat.DateCreated));
    }

    [HttpPost("referral-payouts")]
    public async Task<ActionResult<BillingLedgerEntryRecord>> RecordReferralPayout(
        [FromBody] RecordReferralPayoutRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0) return BadRequest("The amount must be positive.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("The description is required — say which referrals this settles.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Users.AnyAsync(u => u.Id == request.ReferrerAppUserId, ct))
            return NotFound("Referrer not found.");

        var entry = new BillingLedgerEntry
        {
            Id                 = Guid.NewGuid(),
            Kind               = BillingLedgerKind.ReferralPayout,
            ReferrerAppUserId  = request.ReferrerAppUserId,
            Amount             = decimal.Round(request.Amount, 2),
            Description        = request.Description.Trim(),
            PaymentReference   = request.PaymentReference?.Trim(),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = GetCurrentUserId(),
        };
        db.BillingLedgerEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return Ok(await ReadBackAsync(db, entry.Id, ct));
    }
}
