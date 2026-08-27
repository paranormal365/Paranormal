using Ben.Data.Source.Services;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Billing;
using Ben.Service.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The price list, for anybody — the pricing page a visitor reads before signing up.
/// </summary>
/// <remarks>
/// <para>This is the "administered live" half of item 85's contract arc: the page renders whatever
/// the rows say, so a SuperAdmin adding a band, a cadence or a cap changes the public site without
/// a deployment. The contract half — a paid group keeping the terms it bought — lives in
/// <c>EffectiveTermsResolver</c> and never affects what this endpoint advertises, because the
/// price list is an offer to the next buyer, not a statement about existing deals.</para>
///
/// <para><b>Anonymous, and traced on the anonymous path</b> per the standing rule: the reader who
/// matters here is precisely the one who is not signed in.</para>
///
/// <para>An unusable price list (bands that no longer tile) returns an empty list rather than an
/// error: the visitor can do nothing about it, and an empty pricing page is the same "come back
/// later" without a stack of prose. The admin screen is where the problem is reported loudly.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/pricing")]
public sealed class PublicPricingController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public PublicPricingController(IDbContextFactory<BenDataContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PublicSubscriptionTier>>> GetTiers(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tiers = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.Prices).Include(t => t.Limits).Include(t => t.PermissionAreas).Include(t => t.ExcludedCapabilities)
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.MinMembers)
            .ToListAsync(ct);

        if (SubscriptionTierResolver.Validate(tiers) is not null)
            return Ok(Array.Empty<PublicSubscriptionTier>());

        return Ok(tiers.Select(t => new PublicSubscriptionTier(
            t.Id, t.Name, t.MinMembers, t.MaxMembers,
            [.. t.Prices.Where(p => p.IsActive).OrderBy(p => (int)p.Interval)
                .Select(p => new PublicTierPrice(
                    p.Interval, p.Price,
                    SubscriptionPricing.SavingPercentAgainstMonthly(t, p.Interval)))],
            [.. t.Limits.OrderBy(l => (int)l.Limit)
                .Select(l => new PublicTierLimit(l.Limit, l.MaxValue))],
            // Item 156 Phase E: which role areas the plan includes — the pricing page's
            // honest answer to what upgrading actually buys. Zero checklist rows means ALL
            // areas (TierAreaResolution's fail-open rule), which the payload says as null —
            // an empty list here would render as "includes nothing", the exact inversion.
            t.PermissionAreas.Count == 0
                ? null
                : [.. t.PermissionAreas.Select(a => a.Area).OrderBy(a => (int)a)],
            // Item 167: capabilities. Storage is exclusion rows; the payload speaks in
            // inclusions with the same null-means-everything contract as the areas.
            t.ExcludedCapabilities.Count == 0
                ? null
                : [.. Enum.GetValues<Ben.Data.Common.Enums.TierCapability>()
                    .Except(t.ExcludedCapabilities.Select(c => c.Capability)).OrderBy(c => (int)c)],
            t.IsBandedByMembers)));
    }
}
