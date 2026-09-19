using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// One cap as it actually applies to a group right now, and where it came from.
/// </summary>
/// <param name="Limit">What is capped.</param>
/// <param name="MaxValue">The cap. Null is unlimited.</param>
/// <param name="FromContract">
/// True when the group's contract is what is holding this value — the live tier says something
/// worse. The pricing card uses it to say "your current terms until {date}".
/// </param>
public readonly record struct EffectiveLimit(SubscriptionLimit Limit, int? MaxValue, bool FromContract);

/// <summary>
/// The better-of rule: what a paid group is entitled to, given what it bought and what its tier
/// now says.
/// </summary>
/// <remarks>
/// <para>The whole of Ben's "changes only upgrade existing paid accounts" is this one function.
/// Improvements a SuperAdmin makes to a tier reach every group immediately, because live-better
/// wins; reductions reach a group only when a new period opens a new snapshot, because
/// contract-better wins until then. There is no per-change bookkeeping, no flag on the edit, and
/// no way for an edit to forget to honour the rule — the rule is applied at read time,
/// every time.</para>
///
/// <para>Pure and static, like <see cref="CouponMath"/>: the ordering of "better" is exactly the
/// kind of thing that is easy to invert for one shape of cap and never notice, so it is tested by
/// regression in both directions.</para>
/// </remarks>
public static class EffectiveTermsResolver
{
    /// <summary>Serializes a tier's caps for a contract snapshot.</summary>
    /// <remarks>Enum keys as names, not numbers — the JSON outlives deployments, and a renumbered
    /// enum silently re-keying every stored contract is the failure names avoid.</remarks>
    public static string ToJson(IEnumerable<SubscriptionTierLimit> limits) =>
        JsonSerializer.Serialize(limits.ToDictionary(l => l.Limit.ToString(), l => l.MaxValue));

    /// <summary>Reads a snapshot's caps back. Unknown keys are skipped, not fatal —
    /// a limit type retired from the enum must not brick every old contract.</summary>
    public static IReadOnlyDictionary<SubscriptionLimit, int?> FromJson(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, int?>>(json) ?? [];
        var result = new Dictionary<SubscriptionLimit, int?>();

        foreach (var (key, value) in raw)
            if (Enum.TryParse<SubscriptionLimit>(key, out var limit))
                result[limit] = value;

        return result;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is at least as good for the group as
    /// <paramref name="baseline"/>. Null is unlimited, so null beats every number.
    /// </summary>
    public static bool IsAtLeastAsGood(int? candidate, int? baseline) =>
        candidate is null || (baseline is not null && candidate >= baseline);

    /// <summary>
    /// The caps that actually bind a group: each key present in either source, at the better of
    /// the two values. Pass a null contract for a free-band group — nothing was bought, so the
    /// live tier applies as-is.
    /// </summary>
    /// <remarks>
    /// A cap present in only one source applies at that source's value <b>unless the group's
    /// contract predates it</b> — a cap the live tier adds mid-term is a reduction (there was no
    /// cap when they paid), so for a contracted group a live-only cap does not bind until renewal.
    /// The converse — a contract cap the live tier has dropped — stops binding immediately,
    /// because dropping it is an improvement.
    /// </remarks>
    public static IReadOnlyList<EffectiveLimit> Resolve(
        SubscriptionContractTerms? contract, IEnumerable<SubscriptionTierLimit> liveLimits)
    {
        var live = liveLimits.ToDictionary(l => l.Limit, l => l.MaxValue);

        if (contract is null)
            return [.. live.OrderBy(kv => (int)kv.Key)
                .Select(kv => new EffectiveLimit(kv.Key, kv.Value, FromContract: false))];

        var bought = FromJson(contract.LimitsJson);
        var result = new List<EffectiveLimit>();

        foreach (var limit in live.Keys.Union(bought.Keys).OrderBy(l => (int)l))
        {
            var inLive   = live.TryGetValue(limit, out var liveMax);
            var inBought = bought.TryGetValue(limit, out var boughtMax);

            if (!inLive)
                continue;              // dropped from the tier: an improvement, effective now

            if (!inBought)
            {
                // Added since they paid: a reduction, waits for renewal. Not silently dropped —
                // the contract is holding "uncapped" against a live cap, and the card should say
                // "Unlimited X (your current terms)" rather than nothing. Null max means the
                // enforcement outcome is identical to skipping; only the display changes.
                result.Add(new EffectiveLimit(limit, null, FromContract: true));
                continue;
            }

            result.Add(IsAtLeastAsGood(liveMax, boughtMax)
                ? new EffectiveLimit(limit, liveMax, FromContract: false)
                : new EffectiveLimit(limit, boughtMax, FromContract: true));
        }

        return result;
    }

    /// <summary>
    /// The price that binds: the contract's for the rest of the period, unless the live price for
    /// the same band and cadence has dropped below it — a price cut is an improvement too.
    /// </summary>
    public static (decimal Price, bool FromContract) EffectivePrice(
        SubscriptionContractTerms contract, SubscriptionTier liveTier)
    {
        var liveNow = SubscriptionPricing.PriceFor(liveTier, contract.Interval);

        // FromContract means the live value is WORSE and the contract is holding the line — not
        // merely "not better". An unchanged price flagged as held put "the plan has changed since
        // you subscribed" on every fresh subscription's card; caught on the first live look.
        return liveNow switch
        {
            { } live when live < contract.Price  => (live, false),
            { } live when live == contract.Price => (contract.Price, false),
            _                                    => (contract.Price, true),
        };
    }

    /// <summary>Takes the snapshot a newly opened period should store.</summary>
    public static SubscriptionContractTerms Snapshot(
        OrganizationSubscription subscription, SubscriptionTier tier, BillingInterval interval,
        decimal price, DateTime periodStartUtc, DateTime periodEndUtc, Guid byUserId) =>
        new()
        {
            Id                         = Guid.NewGuid(),
            OrganizationSubscriptionId = subscription.Id,
            SubscriptionTierId         = tier.Id,
            TierName                   = tier.Name,
            Interval                   = interval,
            Price                      = price,
            LimitsJson                 = ToJson(tier.Limits),
            PeriodStartUtc             = periodStartUtc,
            PeriodEndUtc               = periodEndUtc,
            DateCreated                = DateTime.UtcNow,
            CreatedByAppUserId         = byUserId,
        };
}
