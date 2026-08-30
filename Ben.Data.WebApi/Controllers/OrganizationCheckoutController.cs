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

        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);
        var tier = SubscriptionTierResolver.Resolve(tiers, members);

        if (SubscriptionPricing.PriceFor(tier, request.Interval) is not { } listPrice)
            return BadRequest($"\"{tier.Name}\" is not offered at that billing cadence.");

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
            listPrice, discount);

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
            $"IsHaunted \"{tier.Name}\" — {members} members, billed {Cadence(request.Interval)}",
            SuccessUrl: $"{billingUrl}?checkout=success",
            CancelUrl:  $"{billingUrl}?checkout=cancelled",
            facts.ToMetadata()), ct);

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
