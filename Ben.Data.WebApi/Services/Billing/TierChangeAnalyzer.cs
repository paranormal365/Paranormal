using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>One consequence of a tier edit, classified and already worded for the reader.</summary>
/// <param name="IsImprovement">
/// True when the change is in the group's favour. Decides delivery, not just wording:
/// improvements are announced immediately, reductions wait to be noticed before renewal.
/// </param>
/// <param name="Sentence">Written for the group receiving it, not for the SuperAdmin.</param>
public readonly record struct TierChange(bool IsImprovement, string Sentence);

/// <summary>
/// What a tier edit means for the groups on it — each change classified as an improvement or a
/// reduction, in the group's own terms.
/// </summary>
/// <remarks>
/// <para><b>Per field, not per save.</b> One edit can raise the equipment cap and lower the storage
/// cap; calling that edit "an improvement" or "a reduction" wholesale would either bury the bad
/// news in a cheerful message or sit on good news for two weeks. Each change travels under its own
/// classification.</para>
///
/// <para><b>The direction convention matches <see cref="EffectiveTermsResolver"/>:</b> null is
/// unlimited, zero is off, higher is better, cheaper is better. If these two ever disagree, a
/// group would be told about a change the resolver does not apply to them, or vice versa — so
/// both are covered by the same regression tests.</para>
///
/// <para>Pure and static, like the rest of the billing arithmetic.</para>
/// </remarks>
public static class TierChangeAnalyzer
{
    /// <summary>Every change between two versions of a tier's terms.</summary>
    public static IReadOnlyList<TierChange> Analyze(
        IReadOnlyDictionary<SubscriptionLimit, int?> oldLimits,
        IReadOnlyDictionary<SubscriptionLimit, int?> newLimits,
        IReadOnlyDictionary<BillingInterval, decimal> oldPrices,
        IReadOnlyDictionary<BillingInterval, decimal> newPrices)
    {
        var changes = new List<TierChange>();

        foreach (var limit in oldLimits.Keys.Union(newLimits.Keys).OrderBy(l => (int)l))
        {
            var had = oldLimits.TryGetValue(limit, out var oldMax);
            var has = newLimits.TryGetValue(limit, out var newMax);

            if (had && !has)
                changes.Add(new TierChange(true, $"The limit on {Noun(limit)} has been removed."));
            else if (!had && has)
                changes.Add(new TierChange(false,
                    $"A limit now applies to {Noun(limit)}: {Amount(limit, newMax)}."));
            else if (had && has && oldMax != newMax)
                changes.Add(EffectiveTermsResolver.IsAtLeastAsGood(newMax, oldMax)
                    ? new TierChange(true,
                        $"The limit on {Noun(limit)} has increased from {Amount(limit, oldMax)} to {Amount(limit, newMax)}.")
                    : new TierChange(false,
                        $"The limit on {Noun(limit)} is changing from {Amount(limit, oldMax)} to {Amount(limit, newMax)}."));
        }

        foreach (var interval in oldPrices.Keys.Union(newPrices.Keys).OrderBy(i => (int)i))
        {
            var had = oldPrices.TryGetValue(interval, out var oldPrice);
            var has = newPrices.TryGetValue(interval, out var newPrice);

            if (had && !has)
                changes.Add(new TierChange(false, $"The plan is no longer offered {Cadence(interval)}."));
            else if (!had && has)
                changes.Add(new TierChange(true,
                    $"The plan can now be billed {Cadence(interval)}, at {newPrice:C2} per period."));
            else if (had && has && oldPrice != newPrice)
                changes.Add(newPrice < oldPrice
                    ? new TierChange(true,
                        $"The {Cadence(interval)} price has dropped from {oldPrice:C2} to {newPrice:C2}.")
                    : new TierChange(false,
                        $"The {Cadence(interval)} price is changing from {oldPrice:C2} to {newPrice:C2}."));
        }

        return changes;
    }

    /// <summary>
    /// The changes between two versions of a tier's included-areas checklist (item 156 Phase E).
    /// Separate from <see cref="Analyze"/> because areas are saved through their own endpoint;
    /// sharing the TierChange shape keeps the notifier's improvement/reduction fan-out identical.
    /// </summary>
    public static IReadOnlyList<TierChange> AnalyzeAreas(
        IReadOnlySet<OrganizationPermissionArea> oldAreas,
        IReadOnlySet<OrganizationPermissionArea> newAreas)
    {
        var changes = new List<TierChange>();
        foreach (var area in oldAreas.Union(newAreas).OrderBy(a => (int)a))
        {
            if (oldAreas.Contains(area) && !newAreas.Contains(area))
                changes.Add(new TierChange(false, AreaReductionSentence(area)));
            else if (!oldAreas.Contains(area) && newAreas.Contains(area))
                changes.Add(new TierChange(true, AreaImprovementSentence(area)));
        }
        return changes;
    }

    /// <summary>
    /// One area's removal sentence, exactly as queued. The notifier matches on this exact string
    /// to cancel a pending notice when the area is re-added before delivery — an uncheck-then-
    /// recheck must net to silence, not to two contradictory messages.
    /// </summary>
    public static string AreaReductionSentence(OrganizationPermissionArea area)
        => $"Custom-role permissions for {AreaNoun(area)} are no longer included in the plan. "
         + "Existing role grants in that area stop applying but are kept, and resume if the area returns.";

    /// <summary>One area's addition sentence.</summary>
    public static string AreaImprovementSentence(OrganizationPermissionArea area)
        => $"Custom-role permissions for {AreaNoun(area)} are now included in the plan.";

    private static string AreaNoun(OrganizationPermissionArea area) => area switch
    {
        OrganizationPermissionArea.OrganizationProfile => "the group profile",
        OrganizationPermissionArea.Membership          => "membership",
        OrganizationPermissionArea.Cases               => "cases",
        OrganizationPermissionArea.Investigations      => "investigations",
        OrganizationPermissionArea.Clients             => "client requests",
        OrganizationPermissionArea.Calendar            => "the calendar",
        OrganizationPermissionArea.Files               => "files",
        OrganizationPermissionArea.Equipment           => "equipment",
        OrganizationPermissionArea.PublicPages         => "public pages",
        _ => area.ToString(),
    };

    /// <summary>The dictionaries <see cref="Analyze"/> wants, from a tier's live rows.</summary>
    public static (IReadOnlyDictionary<SubscriptionLimit, int?> Limits,
                   IReadOnlyDictionary<BillingInterval, decimal> Prices) TermsOf(SubscriptionTier tier) =>
        (tier.Limits.ToDictionary(l => l.Limit, l => l.MaxValue),
         tier.Prices.Where(p => p.IsActive).ToDictionary(p => p.Interval, p => p.Price));

    private static string Noun(SubscriptionLimit limit) => limit switch
    {
        SubscriptionLimit.OpenCases            => "open cases",
        SubscriptionLimit.EquipmentItems       => "equipment",
        SubscriptionLimit.ActiveEquipmentLoans => "equipment loans",
        SubscriptionLimit.OpenInvestigations   => "open investigations",
        SubscriptionLimit.PendingInvites       => "pending invites",
        SubscriptionLimit.StorageMegabytes     => "storage",
        SubscriptionLimit.PublishedPages       => "public pages",
        SubscriptionLimit.CustomRoles          => "custom roles",
        _                                      => limit.ToString(),
    };

    private static string Amount(SubscriptionLimit limit, int? max) => max switch
    {
        null => "unlimited",
        0    => "not included",
        { } n when limit == SubscriptionLimit.StorageMegabytes && n >= 1024 => $"{n / 1024m:0.#} GB",
        { } n when limit == SubscriptionLimit.StorageMegabytes => $"{n} MB",
        { } n => n.ToString("N0"),
    };

    private static string Cadence(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly    => "monthly",
        BillingInterval.Quarterly  => "quarterly",
        BillingInterval.HalfYearly => "every six months",
        BillingInterval.Yearly     => "yearly",
        _                          => interval.ToString().ToLowerInvariant(),
    };
}
