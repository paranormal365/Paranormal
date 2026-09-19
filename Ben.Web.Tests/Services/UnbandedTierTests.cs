using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A tier sold to a KIND of business is never assigned by headcount (item 198).
/// </summary>
/// <remarks>
/// <para>Ghost walking tour companies have three guides and two hundred customers a week, so
/// banding them by staff prices the wrong thing entirely. The tour plan is flat, chosen rather
/// than banded into — and it must be invisible to the member ladder, or it both steals groups
/// that should be on Free and breaks the ladder's contiguity rules by having no place in it.</para>
///
/// <para>The migration that added the flag is the other half of this: EF defaults a new bool
/// column to FALSE, which silently emptied the ladder and made every quote answer 503. It is
/// pinned here because "all tiers left the ladder" reads as a pricing outage, not as a default.</para>
/// </remarks>
public class UnbandedTierTests
{
    private static SubscriptionTier Band(string name, int min, int? max) => new()
    {
        Id = Guid.NewGuid(), Name = name, MinMembers = min, MaxMembers = max,
        SortOrder = min, IsActive = true, IsBandedByMembers = true,
    };

    private static SubscriptionTier Business(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, MinMembers = 1, MaxMembers = null,
        SortOrder = 90, IsActive = true, IsBandedByMembers = false,
    };

    [Fact]
    public void A_business_tier_does_not_break_the_ladders_contiguity()
    {
        // Without the filter this reads as an overlapping/duplicate band and refuses the list.
        var tiers = new[] { Band("Free", 1, 3), Band("Small", 4, 10), Band("Large", 11, null), Business("Tour") };

        Assert.Null(SubscriptionTierResolver.Validate(tiers));
    }

    [Theory]
    [InlineData(1, "Free")]
    [InlineData(4, "Small")]
    [InlineData(50, "Large")]
    public void Headcount_never_lands_on_a_business_tier(int members, string expected)
    {
        var tiers = new[] { Band("Free", 1, 3), Band("Small", 4, 10), Band("Large", 11, null), Business("Tour") };

        Assert.Equal(expected, SubscriptionTierResolver.Resolve(tiers, members).Name);
    }

    /// <summary>A ladder of nothing but business tiers is not a ladder, and says so.</summary>
    [Fact]
    public void A_list_with_no_banded_tiers_is_refused_rather_than_silently_empty()
    {
        var problem = SubscriptionTierResolver.Validate(new[] { Business("Tour") });

        Assert.NotNull(problem);
        Assert.Contains("no active price bands", problem);
    }
}
