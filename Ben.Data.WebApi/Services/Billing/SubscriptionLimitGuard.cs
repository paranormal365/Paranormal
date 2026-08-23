using Ben.Data.Source.Services;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// The one place that answers "may this group have one more of these?".
/// </summary>
/// <remarks>
/// <para><b>Reads effective terms, not the live tier.</b> A paid group's contract may hold a
/// higher cap (or no cap) than the tier now says — <see cref="EffectiveTermsResolver"/> decides,
/// and enforcement reading anything else would break the promise the pricing card makes.</para>
///
/// <para><b>Fail open, deliberately.</b> No subscription row, no tier, an unpriced band, a cap
/// with no row — every ambiguous state answers "allowed". A cap that appears because data is
/// missing would lock paying groups out of features and be reported as the platform being broken;
/// a missing cap costs pennies. The same reasoning as no-row-means-no-cap on the tier itself.</para>
///
/// <para><b>The refusal is a sentence naming the cap and the band</b>, because it will be shown to
/// a person who has to decide what to do next — and per the standing rule, every endpoint that
/// returns it must have a UI path that renders it. The count is "current", passed in by the caller
/// who already knows how to count its own thing; this guard does not own seven different counting
/// queries.</para>
/// </remarks>
public sealed class SubscriptionLimitGuard
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public SubscriptionLimitGuard(IDbContextFactory<BenDataContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Why this group may not add records at all right now, or null when it may.
    /// </summary>
    /// <remarks>
    /// <para>Item 84's read-only rule: a lapsed subscription keeps everything readable and stops
    /// everything new. The test is <b>Status == Lapsed</b>, set by the lapse job — deliberately
    /// not "period end has passed", because with manual billing a group that paid on Tuesday is
    /// recorded on Thursday, and a wall-clock cutoff would read-only groups that paid on time.
    /// The job is the arbiter; this only reads its verdict.</para>
    ///
    /// <para>Fail open like the caps: no subscription row is the free state, not the lapsed one.</para>
    /// </remarks>
    public async Task<string?> WhyReadOnlyAsync(Guid organizationId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var lapsed = await db.OrganizationSubscriptions.AsNoTracking()
            .AnyAsync(s => s.OrganizationId == organizationId
                        && s.Status == Ben.Data.Common.Enums.SubscriptionStatus.Lapsed, ct);

        return lapsed
            ? "Your group's subscription has ended, so nothing new can be added — everything "
            + "already here stays readable. Renewing brings everything back exactly as it was."
            : null;
    }

    /// <summary>
    /// Why this group may not add one more of <paramref name="limit"/>, or null when it may.
    /// </summary>
    /// <param name="organizationId">The group being checked.</param>
    /// <param name="limit">Which cap to test.</param>
    /// <param name="currentCount">How many the group has now, counted by the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string?> WhyNotOneMoreAsync(
        Guid organizationId, SubscriptionLimit limit, int currentCount, CancellationToken ct)
    {
        // Lapsed outranks any cap: "you are at your limit" would send somebody to upgrade a
        // subscription that has actually ended.
        if (await WhyReadOnlyAsync(organizationId, ct) is { } readOnly) return readOnly;

        var effective = await EffectiveLimitAsync(organizationId, limit, ct);

        if (effective.Max is null) return null;                    // uncapped, or nothing configured

        if (effective.Max == 0)
            return $"Your group's plan ({effective.TierName}) does not include {Noun(limit)}. "
                 + "A larger plan does — see the pricing page.";

        if (currentCount >= effective.Max)
            return $"Your group's plan ({effective.TierName}) includes "
                 + $"{Amount(limit, effective.Max.Value)}, and you are using all of it. "
                 + "A larger plan raises the limit — see the pricing page.";

        return null;
    }

    /// <summary>The cap that actually binds, with the band name for the refusal sentence.</summary>
    private async Task<(int? Max, string TierName)> EffectiveLimitAsync(
        Guid organizationId, SubscriptionLimit limit, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId, ct);

        // A group with no subscription row sits on whatever the resolver says its member count
        // buys — which for enforcement means the band the resolver picks, with no contract.
        var tier = sub?.SubscriptionTierId is { } tierId
            ? await db.SubscriptionTiers.AsNoTracking().Include(t => t.Limits)
                .FirstOrDefaultAsync(t => t.Id == tierId, ct)
            : await ResolveByMembersAsync(db, organizationId, ct);

        if (tier is null) return (null, "");

        var contract = sub?.CurrentPeriodStart is { } start
            ? await db.SubscriptionContractTerms.AsNoTracking()
                .Where(t => t.OrganizationSubscriptionId == sub.Id && t.PeriodStartUtc == start)
                .FirstOrDefaultAsync(ct)
            : null;

        var bound = EffectiveTermsResolver.Resolve(contract, tier.Limits)
            .FirstOrDefault(l => l.Limit == limit);

        // default(EffectiveLimit) has MaxValue null — absent means uncapped, which is the point.
        return (bound.Limit == limit ? bound.MaxValue : null, tier.Name);
    }

    private static async Task<Data.Source.Entities.SubscriptionTier?> ResolveByMembersAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Limits).ToListAsync(ct);

        if (SubscriptionTierResolver.Validate(tiers) is not null) return null;   // fail open

        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);

        return SubscriptionTierResolver.Resolve(tiers, members);
    }

    private static string Noun(SubscriptionLimit limit) => limit switch
    {
        SubscriptionLimit.OpenCases            => "more open cases",
        SubscriptionLimit.EquipmentItems       => "equipment tracking",
        SubscriptionLimit.ActiveEquipmentLoans => "equipment lending",
        SubscriptionLimit.OpenInvestigations   => "open investigations",
        SubscriptionLimit.PendingInvites       => "invitations",
        SubscriptionLimit.StorageMegabytes     => "file storage",
        SubscriptionLimit.PublishedPages       => "public pages",
        SubscriptionLimit.CustomRoles          => "custom roles",
        _                                      => limit.ToString(),
    };

    private static string Amount(SubscriptionLimit limit, int max) => limit switch
    {
        SubscriptionLimit.OpenCases            => $"{max} open case(s)",
        SubscriptionLimit.EquipmentItems       => $"{max} piece(s) of equipment",
        SubscriptionLimit.ActiveEquipmentLoans => $"{max} loan(s) out at a time",
        SubscriptionLimit.OpenInvestigations   => $"{max} open investigation(s)",
        SubscriptionLimit.PendingInvites       => $"{max} pending invite(s)",
        SubscriptionLimit.StorageMegabytes     => max >= 1024 ? $"{max / 1024m:0.#} GB of storage" : $"{max} MB of storage",
        SubscriptionLimit.PublishedPages       => $"{max} public page(s)",
        SubscriptionLimit.CustomRoles          => $"{max} custom role(s)",
        _                                      => max.ToString(),
    };
}
