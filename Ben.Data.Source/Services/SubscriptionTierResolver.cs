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
        var bands = tiers.Where(t => t.IsActive).OrderBy(t => t.MinMembers).ToList();

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
                if (band.MaxMembers is not null)
                    return $"The highest band \"{band.Name}\" stops at {band.MaxMembers} members. "
                         + "The top band must be unbounded, or a group can outgrow the price list.";
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

        return tiers
            .Where(t => t.IsActive)
            .OrderBy(t => t.MinMembers)
            .First(t => t.MinMembers <= count && (t.MaxMembers is null || count <= t.MaxMembers));
    }
}
