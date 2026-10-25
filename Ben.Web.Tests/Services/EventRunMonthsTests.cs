using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Extra months for a show's run (Ben, 10/24/2026): half the buyer's own credit price, only for a published
/// show that isn't over, at most eleven, and never a credit.
/// </summary>
public sealed class EventRunMonthsTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static HostedEvent Show(HostedEventLifecycleState state = HostedEventLifecycleState.Published, bool run = true)
        => new() { Id = Guid.NewGuid(), Name = "Murder at the Manor", LifecycleState = state, DatesAreSeparate = run };

    [Fact]
    public void A_month_is_half_of_what_the_buyer_pays_for_a_credit()
    {
        var offer = new EventCreditOffer(true, 99m, 20m, []);
        Assert.Equal(49.50m, EventRunMonths.PriceOfAMonth(offer, subscriber: false, 50m));
        Assert.Equal(9.90m, EventRunMonths.PriceOfAMonth(offer, subscriber: true, 50m));
    }

    [Fact]
    public void A_published_or_running_show_that_isnt_over_can_be_extended_up_to_the_cap()
    {
        Assert.Null(EventRunMonths.WhyNotExtend(Show(), 0, 1, 11, Now.AddDays(5), Now));
        Assert.Null(EventRunMonths.WhyNotExtend(Show(HostedEventLifecycleState.Live), 10, 1, 11, Now.AddDays(5), Now));
    }

    [Theory]
    [InlineData(HostedEventLifecycleState.Draft, "Publish the show first")]
    [InlineData(HostedEventLifecycleState.Ended, "over")]
    [InlineData(HostedEventLifecycleState.Archived, "over")]
    [InlineData(HostedEventLifecycleState.Cancelled, "called off")]
    [InlineData(HostedEventLifecycleState.VenueWithdrawn, "called off")]
    public void Only_a_published_show_that_isnt_over_takes_months(HostedEventLifecycleState state, string said)
        => Assert.Contains(said, EventRunMonths.WhyNotExtend(Show(state), 0, 1, 11, Now.AddDays(5), Now));

    [Fact]
    public void A_stay_never_takes_a_month_and_each_weekend_is_its_own_event()
        => Assert.Contains("another is its own event", EventRunMonths.WhyNotExtend(Show(run: false), 0, 1, 11, Now.AddDays(5), Now));

    [Fact]
    public void A_show_whose_last_performance_has_ended_is_over_whatever_its_state_says()
        => Assert.Contains("over", EventRunMonths.WhyNotExtend(Show(HostedEventLifecycleState.Live), 0, 1, 11, Now.AddHours(-1), Now));

    [Fact]
    public void The_cap_is_said_with_how_many_are_left()
    {
        Assert.Equal("This show has 9 extra months, and one show can have 11. Choose 2 or fewer.",
            EventRunMonths.WhyNotExtend(Show(), 9, 3, 11, Now.AddDays(5), Now));
        Assert.Equal("This show already has 11 extra months, the most one show can have.",
            EventRunMonths.WhyNotExtend(Show(), 11, 1, 11, Now.AddDays(5), Now));
    }

    [Fact]
    public void A_month_is_held_until_applied_refunded_or_lapsed()
    {
        var m = new EventRunMonth { ExpiresUtc = Now.AddDays(10) };
        Assert.True(m.IsHeld(Now));
        Assert.False(m.Counts);
        m.AppliedToHostedEventId = Guid.NewGuid();
        Assert.False(m.IsHeld(Now));
        Assert.True(m.Counts);
        m.RefundedUtc = Now;
        Assert.False(m.Counts);
        Assert.False(new EventRunMonth { ExpiresUtc = Now.AddDays(-1) }.IsHeld(Now));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("50", true)]
    [InlineData("0", false)]
    [InlineData("101", false)]
    public void The_percentage_setting_is_1_to_100(string? raw, bool ok)
        => Assert.Equal(ok, EventRunMonths.WhyNotAPercent(raw) is null);

    [Theory]
    [InlineData(null, true)]
    [InlineData("11", true)]
    [InlineData("0", false)]
    [InlineData("37", false)]
    [InlineData("two", false)]
    public void The_cap_setting_is_1_to_36(string? raw, bool ok)
        => Assert.Equal(ok, EventRunMonths.WhyNotACap(raw) is null);
}
