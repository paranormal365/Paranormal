using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// A group's own view of its subscription: what it is on, and what checking out would cost.
/// </summary>
/// <remarks>
/// <para><b>The quote is the coupon line.</b> Ben's checkout has a line for a coupon before
/// finalization, and this endpoint is that line's whole backend: send the cadence and whatever was
/// typed, get back list price, discount, payable and — when the code cannot be used — the sentence
/// to show beside the box. The screen never computes money and never interprets a status code.</para>
///
/// <para><b>Quoting mutates nothing.</b> Typing a code is not redeeming it. The quote checks every
/// redemption rule so the answer is honest, but the counts move only when the period is actually
/// opened — otherwise an abandoned checkout would burn a single-use code, which for an addressed
/// code means burning somebody's personal discount by curiosity.</para>
///
/// <para>Gated on <see cref="OrganizationSecurityTable.OrganizationSettings"/>: what a group pays
/// is a settings-level fact, and the people who may see the quote are the people who may act on
/// it. Same bar as the settings screen the subscription card sits on.</para>
/// </remarks>
[Route("api/organizations/{organizationId:guid}/subscription")]
public sealed class OrganizationSubscriptionController : OrgCmsControllerBase
{
    public OrganizationSubscriptionController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    /// <summary>Prices one period at one cadence, applying a typed coupon code when one is sent.</summary>
    [HttpPost("quote")]
    public async Task<ActionResult<SubscriptionQuoteResponse>> Quote(
        Guid organizationId, [FromBody] SubscriptionQuoteRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (!await IsCmsAuthorizedAsync(userId.Value, organizationId,
                OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var tiers = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.Prices).ToListAsync(ct);

        if (SubscriptionTierResolver.Validate(tiers) is not null)
        {
            // The price list being broken is a platform problem, not this group's. Refusing with
            // the tiling detail would hand a member a sentence about bands they cannot see or fix.
            return Problem("Pricing is temporarily unavailable.", statusCode: 503);
        }

        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);

        var tier = SubscriptionTierResolver.Resolve(tiers, members);

        if (SubscriptionPricing.PriceFor(tier, request.Interval) is not { } listPrice)
            return BadRequest($"\"{tier.Name}\" is not offered at that billing cadence.");

        var subscription = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

        // ── the coupon line ──────────────────────────────────────────────────
        var typed = CouponCodeGenerator.Normalise(request.CouponCode);
        if (typed.Length == 0)
            return Ok(new SubscriptionQuoteResponse(
                tier.Name, request.Interval, listPrice, 0m, listPrice, null, null));

        var code = await db.CouponCodes.AsNoTracking()
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.Code == typed, ct);

        // The same sentence as every other dead code, on purpose: "no such code" and "withdrawn
        // code" being distinguishable would let anybody probe which strings exist.
        if (code is null)
            return Ok(new SubscriptionQuoteResponse(
                tier.Name, request.Interval, listPrice, 0m, listPrice,
                "That code is no longer available.", null));

        var alreadyRedeemed = await db.CouponRedemptions.AsNoTracking()
            .AnyAsync(r => r.CouponId == code.CouponId && r.OrganizationId == organizationId, ct);

        var ctx = new CouponRedemptionContext(
            DateTime.UtcNow, userId.Value, request.Interval,
            IsRenewal: subscription is not null && CouponMath.IsRenewal(subscription),
            AlreadyRedeemedByThisOrg: alreadyRedeemed);

        if (CouponMath.WhyNotRedeemable(code.Coupon, code, ctx) is { } refusal)
            return Ok(new SubscriptionQuoteResponse(
                tier.Name, request.Interval, listPrice, 0m, listPrice, refusal, null));

        var price = CouponMath.PriceFor(listPrice, code.Coupon);

        return Ok(new SubscriptionQuoteResponse(
            tier.Name, request.Interval, price.ListPrice, price.Discount, price.Payable,
            null, CouponMath.PeriodsFor(code.Coupon)));
    }
}
