using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The door money walks through: turns a quote the group accepted into a Stripe Checkout session.
/// </summary>
/// <remarks>
/// <para><b>Everything is priced HERE, before Stripe hears about it.</b> Tier from the live member
/// count, cadence price, coupon, tax — the same arithmetic the quote endpoint shows — and the
/// results are frozen into the session's metadata. The card form the person sees belongs to
/// Stripe; the amounts on it belong to this method. Stripe is the arm, never the brain.</para>
///
/// <para><b>Card data never touches this server.</b> The response is a URL on Stripe's domain;
/// the number is typed there, vaulted there, and comes back to us only as an opaque token the
/// renewal job can charge. That fact is the site's entire PCI posture (SAQ A), so no future
/// version of this flow may accept card fields, however convenient.</para>
///
/// <para><b>Update, not Read.</b> The quote is readable by anyone who can see the settings screen;
/// spending the group's money is an act, and takes the same permission as changing its settings.</para>
/// </remarks>
[Route("api/organizations/{organizationId:guid}/subscription/checkout")]
public sealed class OrganizationCheckoutController : OrgCmsControllerBase
{
    private readonly IStripeGateway _stripe;
    private readonly StripeFulfillmentService _fulfillment;
    private readonly IConfiguration _configuration;

    public OrganizationCheckoutController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        IStripeGateway stripe, StripeFulfillmentService fulfillment, IConfiguration configuration)
        : base(dbFactory, mapper, security)
    {
        _stripe = stripe;
        _fulfillment = fulfillment;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<ActionResult<StartCheckoutResponse>> Start(
        Guid organizationId, [FromBody] StartCheckoutRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return NotFound();

        // ── price it, exactly as the quote did ───────────────────────────────
        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices).ToListAsync(ct);
        if (SubscriptionTierResolver.Validate(tiers) is not null)
            return Problem("Pricing is temporarily unavailable.", statusCode: 503);

        // Members for a group, tours for a business (item 233) — counted in the one place the
        // quote counts them, so what was shown is what is charged.
        if (await BillableUnits.PriceAsync(db, tiers, organizationId, org.Kind, request.Interval, ct)
            is not { } priced)
            return BadRequest("That plan is not offered at that billing cadence.");
        var tier = priced.Tier;
        var members = priced.Members;
        var listPrice = priced.ListPrice;

        // ── A group is never free (Ben, 2026-09-05) ──────────────────────────
        // "An individual can be free... a group cannot." A band priced at zero was still sellable
        // here, and selling it wrote a real Active subscription with no card — which every paywall
        // on the site reads as paid. One click lifted the member limit, the storage cap, private
        // field sessions and private event evidence. The free state for a group is having NO
        // subscription, not holding one that costs nothing, so there is nothing here to buy.
        //
        // Checked against the LIST price, not the payable one: a coupon that discounts a real
        // price to zero is item 195's trial and still goes through the free-fulfilment path below.
        if (listPrice <= 0m)
            return BadRequest(
                "There is nothing to subscribe to at that size. A group is free by not having a "
                + "plan at all — being on one is what a paid plan means. If your group needs a "
                + "plan, the price list is where its bands are set.");

        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

        // The coupon is validated NOW so a bad code refuses before anyone reaches a card form —
        // but redeemed only at fulfillment, where the money is recorded, like the manual path.
        var payable = listPrice;
        var discount = 0m;
        if (!string.IsNullOrWhiteSpace(request.CouponCode))
        {
            var typed = CouponCodeGenerator.Normalise(request.CouponCode);
            var code = await db.CouponCodes.AsNoTracking().Include(c => c.Coupon)
                .FirstOrDefaultAsync(c => c.Code == typed, ct);
            if (code is null) return BadRequest("That code is no longer available.");

            var alreadyRedeemed = await db.CouponRedemptions.AsNoTracking()
                .AnyAsync(r => r.CouponId == code.CouponId && r.OrganizationId == organizationId, ct);
            var ctx = new CouponRedemptionContext(
                DateTime.UtcNow, userId.Value, request.Interval,
                IsRenewal: sub is not null && CouponMath.IsRenewal(sub),
                AlreadyRedeemedByThisOrg: alreadyRedeemed);
            if (CouponMath.WhyNotRedeemable(code.Coupon, code, ctx) is { } refusal)
                return BadRequest(refusal);

            var price = CouponMath.PriceFor(listPrice, code.Coupon);
            payable  = price.Payable;
            discount = price.Discount;
        }

        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, organizationId, ct);
        var tax = TaxResolver.TaxOn(payable, taxRate);

        var facts = new StripeFulfillmentService.CheckoutFacts(
            organizationId, tier.Id, request.Interval, members,
            payable, taxRate, tax, userId.Value,
            string.IsNullOrWhiteSpace(request.CouponCode) ? null : request.CouponCode.Trim(),
            listPrice, discount, TourCount: tier.IsBandedByMembers ? 0 : priced.Units);

        var baseUrl = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        // Back to the billing page either way: the person left it to pay, and landing them on
        // the public group page instead would make a successful payment feel like a wrong turn.
        var billingUrl = $"{baseUrl}/organizations/{organizationId}/billing";

        // ── the 100%-off period: real subscription, no card ──────────────────
        // A free trial coupon prices the period at zero, and Stripe refuses zero-amount
        // sessions — rightly, there is nothing to collect. The item-195 rule says a free period
        // still gets its ledger row and its opened period, so fulfill directly.
        if (payable == 0m)
        {
            await _fulfillment.FulfillAsync(new StripeCompletedCheckout(
                SessionId: $"free-{Guid.NewGuid():N}",
                PaymentIntentRef: null, CustomerRef: null, PaymentMethodRef: null,
                facts.ToMetadata()), ct);
            return Ok(new StartCheckoutResponse($"{billingUrl}?checkout=free", PaidWithoutCharge: true));
        }

        if (!_stripe.IsConfigured)
            return Problem("Online payment isn't set up yet — contact us and we'll sort your group out directly.",
                statusCode: 503);

        var handle = await _stripe.CreateCheckoutSessionAsync(new StripeCheckoutSpec(
            organizationId, org.Name, sub?.ProviderCustomerRef,
            payable, tax,
            $"IsHaunted \"{tier.Name}\" — {BillableUnits.Describe(priced)}, billed {Cadence(request.Interval)}",
            SuccessUrl: $"{billingUrl}?checkout=success",
            CancelUrl:  $"{billingUrl}?checkout=cancelled",
            facts.ToMetadata()), ct);

        return Ok(new StartCheckoutResponse(handle.SessionUrl, PaidWithoutCharge: false));
    }

    /// <summary>
    /// Buying event credits — one credit, one event (item 235).
    /// </summary>
    /// <remarks>
    /// <para><b>Not a subscription.</b> A credit is bought once, lasts a year, and is spent when an
    /// event is published. There is no period to open, nothing to renew, and no tier involved — so
    /// it takes its own path rather than being bent through the subscription machinery, and the
    /// fulfilment side does the same.</para>
    ///
    /// <para><b>The settings key</b>, because this spends the group's money, and the same
    /// permission opens the billing page where the receipt lands.</para>
    ///
    /// <para><b>No free path.</b> Unlike a subscription there is no coupon and no zero-priced
    /// case: a credit either costs what it costs or it is not sold, and a zero-priced credit
    /// would be a paid feature given away by a misconfigured setting.</para>
    /// </remarks>
    [HttpPost("event-credits")]
    public async Task<ActionResult<StartCheckoutResponse>> StartEventCreditCheckout(
        Guid organizationId, [FromQuery] int quantity, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        var count = Math.Clamp(quantity <= 0 ? 1 : quantity, 1, Services.Events.EventCredits.MaximumPerPurchase);

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return NotFound();

        var settings = HttpContext.RequestServices.GetRequiredService<Services.SiteSettingsService>();
        if (!await settings.GetBoolAsync(Services.SiteSettingKeys.EventCreditsEnabled, whenUnset: true, ct))
            return BadRequest("Event credits aren't on sale at the moment.");

        var unit = await settings.GetDecimalAsync(
            Services.SiteSettingKeys.EventCreditPriceUsd, Services.Events.EventCredits.DefaultPriceUsd, ct);

        if (unit <= 0m)
            return Problem("Event credits aren't priced yet.", statusCode: 503);

        var listPrice = unit * count;
        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, organizationId, ct);
        var tax = TaxResolver.TaxOn(listPrice, taxRate);

        if (!_stripe.IsConfigured)
            return Problem("Online payment isn't set up yet — contact us and we'll sort your group out directly.",
                statusCode: 503);

        var baseUrl = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        var billingUrl = $"{baseUrl}/organizations/{organizationId}/billing";

        var metadata = new Dictionary<string, string>
        {
            [StripeFulfillmentService.CheckoutFacts.Keys.Organization] = organizationId.ToString(),
            [StripeFulfillmentService.CheckoutFacts.Keys.User] = userId.Value.ToString(),
            [StripeFulfillmentService.CheckoutFacts.Keys.EventCredits] = count.ToString(),
            [StripeFulfillmentService.CheckoutFacts.Keys.List] =
                listPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [StripeFulfillmentService.CheckoutFacts.Keys.TaxRate] =
                taxRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [StripeFulfillmentService.CheckoutFacts.Keys.TaxAmount] =
                tax.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, ct);

        var handle = await _stripe.CreateCheckoutSessionAsync(new StripeCheckoutSpec(
            organizationId, org.Name, sub?.ProviderCustomerRef,
            listPrice, tax,
            count == 1 ? "IsHaunted — 1 event credit" : $"IsHaunted — {count} event credits",
            SuccessUrl: $"{billingUrl}?credits=success",
            CancelUrl:  $"{billingUrl}?credits=cancelled",
            metadata), ct);

        return Ok(new StartCheckoutResponse(handle.SessionUrl, PaidWithoutCharge: false));
    }

    /// <summary>
    /// The seat-holder pays for their own overflow seat (item 144 meets Stripe).
    /// </summary>
    /// <remarks>
    /// <para>Gated on HOLDING the seat, not on settings permission — the exact rule of
    /// <c>GetMySeats</c>: a seat is a bill addressed to one person, and an ordinary member
    /// paying their own way must not need the group's keys to do it.</para>
    /// <para>The price is the one frozen on the seat at offer time — no re-pricing, no coupons,
    /// no interval choice. The offer said what the ride costs; this button is only "yes".</para>
    /// </remarks>
    [HttpPost("my-seat")]
    public async Task<ActionResult<StartCheckoutResponse>> StartSeatCheckout(
        Guid organizationId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return NotFound();

        var seat = await db.MemberSeatSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId && s.AppUserId == userId, ct);
        if (seat is null) return NotFound("You don't hold a seat in this group.");
        if (seat.Status == SubscriptionStatus.Active
            && seat.CurrentPeriodEnd is { } end && end > DateTime.UtcNow)
            return BadRequest($"Your seat is already paid through {end:MM/dd/yyyy}.");

        if (!_stripe.IsConfigured)
            return Problem("Online payment isn't set up yet — contact us and we'll sort your seat out directly.",
                statusCode: 503);

        var (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, organizationId, ct);
        var tax = TaxResolver.TaxOn(seat.PriceAtStart, taxRate);

        var baseUrl = (_configuration["AppBaseUrl"] ?? "").TrimEnd('/');
        var billingUrl = $"{baseUrl}/organizations/{organizationId}/billing";

        var handle = await _stripe.CreateCheckoutSessionAsync(new StripeCheckoutSpec(
            organizationId,
            // The customer is the MEMBER, so the name Stripe files the card under is theirs.
            $"{org.Name} — member seat",
            seat.ProviderCustomerRef,
            seat.PriceAtStart, tax,
            $"IsHaunted member seat in {org.Name}, billed {Cadence(seat.Interval)}",
            SuccessUrl: $"{billingUrl}?checkout=seat-paid",
            CancelUrl:  $"{billingUrl}?checkout=cancelled",
            new Dictionary<string, string>
            {
                [StripeFulfillmentService.CheckoutFacts.Keys.Seat] = seat.Id.ToString(),
            }), ct);

        return Ok(new StartCheckoutResponse(handle.SessionUrl, PaidWithoutCharge: false));
    }

    private static string Cadence(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly    => "monthly",
        BillingInterval.Quarterly  => "quarterly",
        BillingInterval.HalfYearly => "every six months",
        BillingInterval.Yearly     => "yearly",
        _                          => interval.ToString().ToLowerInvariant(),
    };
}
