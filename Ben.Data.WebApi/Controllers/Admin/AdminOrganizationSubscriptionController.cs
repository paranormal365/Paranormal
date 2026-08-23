using Ben.Data.Source.Services;
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
/// Where every organization stands with the platform, and the manual way to change it.
/// </summary>
/// <remarks>
/// <para><b>This is the manual payment provider.</b> No money moves through the platform yet, so
/// somebody has to be able to say "this group is paid up until March" — and that somebody is a
/// SuperAdmin. The endpoint stays useful once Square or PayPal is wired in, because every provider
/// produces cases it cannot express: a refund, a comped account, a group that paid by cheque.</para>
///
/// <para><b>The list shows the current member count beside the frozen one.</b> The frozen count is
/// what the group is billed on; the current one is what it will be re-banded on at renewal. The gap
/// between them is the only interesting thing on the row, and a screen showing one without the
/// other invites the wrong conclusion.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/organization-subscriptions")]
public sealed class AdminOrganizationSubscriptionController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IAuditLogService _auditLog;

    public AdminOrganizationSubscriptionController(
        IDbContextFactory<BenDataContext> dbFactory, IAuditLogService auditLog)
    {
        _dbFactory = dbFactory;
        _auditLog  = auditLog;
    }

    /// <summary>
    /// Every organization, whether or not it has a subscription row yet.
    /// </summary>
    /// <remarks>
    /// Groups with no row are included deliberately. A list of subscriptions would hide exactly the
    /// organizations that need attention, and "which groups have never been set up?" is the
    /// question this screen exists to answer first.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationSubscriptionAdminRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices).ToListAsync(ct);

        var rows = await db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .Select(o => new
            {
                Organization = o,
                Subscription = db.OrganizationSubscriptions
                    .FirstOrDefault(s => s.OrganizationId == o.Id),
                MemberCount = db.OrganizationUserMemberships
                    .Count(m => m.OrganizationId == o.Id && m.IsActive),
            })
            .ToListAsync(ct);

        // Resolving throws on an unusable price list, and one unusable list must not blank the
        // whole screen. Checked once here so the per-row lookup can be a plain dictionary read.
        var listIsUsable = SubscriptionTierResolver.Validate(tiers) is null;

        // r.Subscription is null for every group that has never been set up — which is the row
        // this screen exists to surface, so it must not be the row that throws.
        return Ok(rows.Select(r => ToRecord(
            r.Organization, r.Subscription, r.MemberCount,
            r.Subscription is null ? null : tiers.FirstOrDefault(t => t.Id == r.Subscription.SubscriptionTierId),
            listIsUsable ? SubscriptionTierResolver.Resolve(tiers, r.MemberCount).Name : null)));
    }

    [HttpGet("{organizationId:guid}")]
    public async Task<ActionResult<OrganizationSubscriptionAdminRecord>> GetOne(
        Guid organizationId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return NotFound();

        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices).ToListAsync(ct);
        var sub   = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);
        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);

        return Ok(ToRecord(org, sub, members,
            tiers.FirstOrDefault(t => t.Id == sub?.SubscriptionTierId),
            SubscriptionTierResolver.Validate(tiers) is null
                ? SubscriptionTierResolver.Resolve(tiers, members).Name
                : null));
    }

    /// <summary>Sets an organization's subscription by hand, creating the row if there is none.</summary>
    [HttpPut("{organizationId:guid}")]
    public async Task<ActionResult<OrganizationSubscriptionAdminRecord>> Set(
        Guid organizationId, [FromBody] SetOrganizationSubscriptionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return NotFound();

        if (request.CurrentPeriodStart is { } start && request.CurrentPeriodEnd is { } end && end <= start)
            return BadRequest("That period ends before it begins.");

        if (request.Status != SubscriptionStatus.Free && request.SubscriptionTierId is null)
            return BadRequest("A paid subscription needs a band to be priced on.");

        var tiers = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.Prices).Include(t => t.Limits).ToListAsync(ct);
        var tier  = tiers.FirstOrDefault(t => t.Id == request.SubscriptionTierId);

        if (request.SubscriptionTierId is not null && tier is null)
            return BadRequest("That band does not exist.");

        if (tier is not null && SubscriptionPricing.PriceFor(tier, request.Interval) is null)
            return BadRequest($"\"{tier.Name}\" is not sold {Cadence(request.Interval)}.");

        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);

        var now = DateTime.UtcNow;
        var sub = await db.OrganizationSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

        // ── the coupon line, now with a real redemption behind it ────────────
        // The quote never redeems; THIS is where a code is spent, because this is where the
        // payment is recorded. Validated with the same rules the quote showed, so what the
        // person was told is what happens.
        CouponCode? code = null;
        PeriodPrice? discounted = null;
        if (!string.IsNullOrWhiteSpace(request.CouponCode) && request.Status == SubscriptionStatus.Active)
        {
            var typed = CouponCodeGenerator.Normalise(request.CouponCode);
            code = await db.CouponCodes.Include(c => c.Coupon)
                .FirstOrDefaultAsync(c => c.Code == typed, ct);

            if (code is null) return BadRequest("That coupon code does not exist.");

            var alreadyRedeemed = await db.CouponRedemptions
                .AnyAsync(r => r.CouponId == code.CouponId && r.OrganizationId == organizationId, ct);

            var ctx = new CouponRedemptionContext(
                now, userId, request.Interval,
                IsRenewal: sub is not null && CouponMath.IsRenewal(sub),
                AlreadyRedeemedByThisOrg: alreadyRedeemed);

            if (CouponMath.WhyNotRedeemable(code.Coupon, code, ctx) is { } refusal)
                return BadRequest(refusal);

            var listPrice = tier is null ? 0m : SubscriptionPricing.PriceFor(tier, request.Interval) ?? 0m;
            discounted = CouponMath.PriceFor(listPrice, code.Coupon);
        }
        var isNew = sub is null;

        sub ??= new OrganizationSubscription
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = organizationId,
            DateCreated        = now,
            CreatedByAppUserId = userId,
        };

        // Same-type clone for the audit diff — AuditChangeTracker refuses anonymous objects.
        var before = new OrganizationSubscription
        {
            Id                 = sub.Id,
            Status             = sub.Status,
            SubscriptionTierId = sub.SubscriptionTierId,
            Interval           = sub.Interval,
            CurrentPeriodStart = sub.CurrentPeriodStart,
            CurrentPeriodEnd   = sub.CurrentPeriodEnd,
            CancelAtPeriodEnd  = sub.CancelAtPeriodEnd,
        };

        // PeriodOpener carries the whole contract rule-set — freeze count and price, snapshot
        // terms from the LIVE tier (which is how a queued reduction lands at renewal), set
        // first-paid exactly once. The provider webhook will call the same method; two copies of
        // this list would disagree within a month.
        sub.CancelAtPeriodEnd = request.CancelAtPeriodEnd;
        var snapshot = PeriodOpener.Open(
            sub, tier, request.Status, request.Interval,
            request.CurrentPeriodStart, request.CurrentPeriodEnd, members, userId);
        sub.ProviderName = "Manual";

        if (snapshot is not null)
        {
            await PeriodOpener.ReplaceSnapshotAsync(db, sub.Id, snapshot.PeriodStartUtc, ct);
            db.SubscriptionContractTerms.Add(snapshot);
        }

        if (code is not null && discounted is { } price)
        {
            // The discount applies to what is actually charged this period, and the redemption
            // records the money AS OF NOW — reimbursement math must survive later price edits.
            sub.PriceAtPeriodStart = price.Payable;
            if (snapshot is not null) snapshot.Price = price.Payable;

            db.CouponRedemptions.Add(new CouponRedemption
            {
                Id                 = Guid.NewGuid(),
                CouponId           = code.CouponId,
                CouponCodeId       = code.Id,
                OrganizationId     = organizationId,
                PeriodsRemaining   = CouponMath.PeriodsFor(code.Coupon) is { } periods ? periods - 1 : null,
                RedeemedAtUtc      = now,
                ListPrice          = price.ListPrice,
                Discount           = price.Discount,
                Payable            = price.Payable,
                DateCreated        = now,
                CreatedByAppUserId = userId,
            });

            // Fast-path counters; the unique index on (CouponId, OrganizationId) is the authority.
            code.RedemptionCount++;
            code.Coupon.RedemptionCount++;
        }

        if (isNew) db.OrganizationSubscriptions.Add(sub);
        else { sub.DateUpdated = now; sub.UpdatedByAppUserId = userId; }

        // Reactivation un-pauses what the lapse paused — and ONLY that. StatusBeforePause marks
        // exactly the cases the lapse job suspended, each of which resumes its own prior status;
        // a case paused for any other future reason has no marker and is left alone. This is the
        // "everything comes back exactly as it was" half of item 84's promise.
        if (before.Status == SubscriptionStatus.Lapsed && request.Status == SubscriptionStatus.Active)
            await PeriodOpener.RestorePausedCasesAsync(db, organizationId, now, ct);

        await db.SaveChangesAsync(ct);

        if (isNew)
            await _auditLog.LogCreateAsync(nameof(OrganizationSubscription), sub.Id, sub, userId, AppSources.WebApi);
        else
            await _auditLog.LogUpdateAsync(nameof(OrganizationSubscription), sub.Id, before, sub, userId, AppSources.WebApi);

        return Ok(ToRecord(org, sub, members, tier,
            SubscriptionTierResolver.Validate(tiers) is null
                ? SubscriptionTierResolver.Resolve(tiers, members).Name
                : null));
    }

    private static string Cadence(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly    => "monthly",
        BillingInterval.Quarterly  => "quarterly",
        BillingInterval.HalfYearly => "every six months",
        BillingInterval.Yearly     => "yearly",
        _                          => interval.ToString().ToLowerInvariant(),
    };

    private static OrganizationSubscriptionAdminRecord ToRecord(
        Organization org, OrganizationSubscription? sub, int currentMembers,
        SubscriptionTier? tier, string? resolvedTierName) =>
        new(sub?.Id ?? Guid.Empty, org.Id, org.Name,
            sub?.Status ?? SubscriptionStatus.Free,
            sub?.SubscriptionTierId, tier?.Name,
            sub?.Interval ?? BillingInterval.Monthly,
            sub?.MemberCountAtPeriodStart ?? 0, currentMembers, resolvedTierName,
            sub?.PriceAtPeriodStart ?? 0m,
            sub?.CurrentPeriodStart, sub?.CurrentPeriodEnd,
            sub?.CancelAtPeriodEnd ?? false, sub?.LapsedAtUtc, sub?.FirstPaidPeriodStartUtc,
            sub?.ProviderName);
}
