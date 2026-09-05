using Ben.Data.Source.Entities;

// Moved from Ben.Data.WebApi.Services.Billing (item 156 Phase D): the security service
// needs tier resolution for the area gate, and this was always pure logic over entities.
namespace Ben.Data.Source.Services;

/// <summary>
/// Turns a member count into the band that prices it, and refuses price lists that cannot.
/// </summary>
/// <remarks>
/// <para><b>Why validation belongs here and not in the editor.</b> The bands are rows a SuperAdmin
/// can change, and the failure they invite is silent: delete the 4–10 band and a five-member group
/// is not "unpriced", it simply matches nothing and is billed for nothing. Nobody notices a group
/// that stops being charged. So the resolver checks the whole list every time it is asked, rather
/// than trusting the screen that wrote it.</para>
///
/// <para>The rules a price list must satisfy: start at one member, leave no gap between bands,
/// never overlap, and end with an unbounded band so a group cannot outgrow the list.</para>
/// </remarks>
public static class SubscriptionTierResolver
{
    /// <summary>What is wrong with a price list, or null when it is sound.</summary>
    /// <remarks>
    /// Returns the first problem rather than all of them: a list with a gap usually has one edit
    /// behind it, and a page of complaints about one mistake is harder to act on than a sentence.
    /// </remarks>
    public static string? Validate(IReadOnlyList<SubscriptionTier> tiers)
    {
        // Only the LADDER is validated for contiguity. A tier sold to a kind of business rather
        // than a size of team (item 198) has no place in the ladder to be contiguous with, and
        // including it would report a gap that is not one.
        var bands = tiers.Where(t => t.IsActive && t.IsBandedByMembers)
                         .OrderBy(t => t.MinMembers).ToList();

        if (bands.Count == 0)
            return "There are no active price bands, so no organization can be priced.";

        if (bands[0].MinMembers != 1)
            return $"The lowest band starts at {bands[0].MinMembers} members; it must start at 1, "
                 + "or a one-member group matches nothing.";

        for (var i = 0; i < bands.Count; i++)
        {
            var band = bands[i];

            if (band.MaxMembers is { } max && max < band.MinMembers)
                return $"\"{band.Name}\" covers {band.MinMembers}–{max}, which is backwards.";

            var isLast = i == bands.Count - 1;

            if (isLast)
            {
                // Item 144: a band that prices extra members is ALLOWED to be outgrown — growth
                // past its cap is billed per seat, not by a bigger band. That is the one legal
                // way for the top band to be bounded.
                if (band.MaxMembers is not null && !AllowsOverflow(band))
                    return $"The highest band \"{band.Name}\" stops at {band.MaxMembers} members. "
                         + "The top band must be unbounded — or price extra members, so growth "
                         + "past it is billed per seat.";
                break;
            }

            if (band.MaxMembers is null)
                return $"\"{band.Name}\" is unbounded but is not the highest band, so it swallows "
                     + "every band above it.";

            var next = bands[i + 1];

            if (next.MinMembers <= band.MaxMembers)
                return $"\"{band.Name}\" and \"{next.Name}\" overlap at {next.MinMembers} members.";

            if (next.MinMembers != band.MaxMembers + 1)
                return $"Nothing prices {band.MaxMembers + 1}"
                     + (next.MinMembers - 1 > band.MaxMembers + 1 ? $"–{next.MinMembers - 1}" : string.Empty)
                     + $" members, between \"{band.Name}\" and \"{next.Name}\".";
        }

        return null;
    }

    /// <summary>
    /// The band that prices <paramref name="memberCount"/>.
    /// </summary>
    /// <remarks>
    /// Throws on an unsound price list rather than returning null. A caller handed null would
    /// almost certainly treat it as "free", which is the expensive direction to be wrong in and
    /// exactly what <see cref="Validate"/> exists to prevent reaching production.
    /// </remarks>
    public static SubscriptionTier Resolve(IReadOnlyList<SubscriptionTier> tiers, int memberCount)
    {
        if (Validate(tiers) is { } problem)
            throw new InvalidOperationException($"The subscription price list is not usable: {problem}");

        // A group with no active members still sits in the lowest band. Zero is a real state —
        // everyone left, or nobody has accepted yet — and it costs whatever one member costs.
        var count = Math.Max(1, memberCount);

        // Only the LADDER is validated for contiguity. A tier sold to a kind of business rather
        // than a size of team (item 198) has no place in the ladder to be contiguous with, and
        // including it would report a gap that is not one.
        var bands = tiers.Where(t => t.IsActive && t.IsBandedByMembers)
                         .OrderBy(t => t.MinMembers).ToList();

        // Item 144: a count beyond a bounded-but-overflowing top band still resolves to that
        // band — the extra members are the overflow seats, not a bigger group price.
        return bands.FirstOrDefault(t => t.MinMembers <= count && (t.MaxMembers is null || count <= t.MaxMembers))
            ?? bands[^1];
    }

    /// <summary>
    /// Why this price list still offers a group something for nothing, or null when it does not.
    /// </summary>
    /// <remarks>
    /// <para><b>Ben's rule, 2026-09-05:</b> "I don't think a 'free group' should ever be a
    /// subscribable thing. An individual can be free... a group cannot." A group's free state is
    /// having no subscription; a band priced at zero is a subscription that costs nothing, and
    /// holding one reads as paid to every gate that asks.</para>
    ///
    /// <para><b>Advisory, deliberately — not part of <see cref="Validate"/>.</b> Validate's verdict
    /// makes <see cref="Resolve"/> throw, and a database whose ladder already carries a zero band
    /// would stop being able to price anything at all: checkout, the area gate and the renewal job
    /// would all fail at once. A pricing mistake must not become an outage. So this reports, the
    /// editor shows it, and the point of sale is where the refusal actually bites.</para>
    /// </remarks>
    public static string? WhyGroupsCanStillBeFree(IReadOnlyList<SubscriptionTier> tiers)
    {
        var free = tiers
            .Where(t => t.IsActive && t.IsBandedByMembers)
            .FirstOrDefault(t => t.Prices.Any(p => p.IsActive && p.Price <= 0m));

        if (free is null) return null;

        var range = free.MaxMembers is { } max ? $"{free.MinMembers}–{max}" : $"{free.MinMembers}+";

        return $"\"{free.Name}\" ({range} members) is priced at nothing, so a group that size can "
             + "hold a subscription without paying — and a subscription is what every paid feature "
             + "checks. A group is free by having no plan, not by being on a free one. Give the "
             + "band a price, or take it out of the ladder.";
    }

    /// <summary>Whether growth past this band is priced per extra member (item 144).</summary>
    public static bool AllowsOverflow(SubscriptionTier tier)
        => tier.Prices.Any(p => p.IsActive && p.PricePerExtraMember is not null);
}
