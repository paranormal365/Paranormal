using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Relative time, and the hour rotation behind the dashboard's histogram (Ben, 2026-08-31).
/// </summary>
/// <remarks>
/// Both exist because a SuperAdmin reading the sign-in panels should not be doing arithmetic. The
/// rotation is the part worth testing hard: a negative UTC offset producing a negative index is
/// the arithmetic everybody gets wrong once, and the US is entirely negative offsets.
/// </remarks>
public sealed class RelativeTimeTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(90, "a minute ago")]
    [InlineData(60 * 5, "5 minutes ago")]
    [InlineData(60 * 90, "an hour ago")]
    [InlineData(60 * 60 * 5, "5 hours ago")]
    [InlineData(60 * 60 * 30, "yesterday")]
    [InlineData(60 * 60 * 24 * 3, "3 days ago")]
    [InlineData(60 * 60 * 24 * 10, "last week")]
    public void Elapsed_time_reads_the_way_somebody_would_say_it(int secondsAgo, string expected)
        => Assert.Equal(expected, Now.AddSeconds(-secondsAgo).ToRelativeTime(Now));

    /// <summary>
    /// A viewer's clock running a little ahead of the server must not produce "in -3 seconds".
    /// </summary>
    [Fact]
    public void A_moment_in_the_future_reads_as_just_now()
        => Assert.Equal("just now", Now.AddSeconds(20).ToRelativeTime(Now));

    // ── the histogram rotation ───────────────────────────────────────────────
    //
    // Mirrors the arithmetic in AdminDashboard.LocalHours. Tested here rather than in the page
    // because it is the part that can be silently wrong: a chart with the right shape sitting six
    // hours out looks entirely plausible.

    private static int UtcHourFor(int localHour, int offset) => ((localHour - offset) % 24 + 24) % 24;

    [Fact]
    public void A_zero_offset_leaves_every_bucket_where_it_was()
    {
        for (var hour = 0; hour < 24; hour++)
            Assert.Equal(hour, UtcHourFor(hour, 0));
    }

    /// <summary>
    /// US Central in summer is UTC-5, so 3 AM local is 8 AM UTC — the case the panel is actually
    /// read in.
    /// </summary>
    [Fact]
    public void A_negative_offset_maps_local_hours_onto_the_right_utc_buckets()
    {
        Assert.Equal(8, UtcHourFor(3, -5));
        Assert.Equal(5, UtcHourFor(0, -5));
        Assert.Equal(4, UtcHourFor(23, -5));
    }

    [Fact]
    public void A_positive_offset_wraps_the_other_way()
    {
        Assert.Equal(1, UtcHourFor(3, 2));
        Assert.Equal(22, UtcHourFor(0, 2));
    }

    /// <summary>
    /// Every offset must land every local hour on a real bucket. A negative index is an
    /// IndexOutOfRange at render time, which is a dashboard that will not draw at all.
    /// </summary>
    [Theory]
    [InlineData(-12)]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(14)]
    public void Every_offset_produces_a_valid_bucket_for_every_hour(int offset)
    {
        var mapped = Enumerable.Range(0, 24).Select(h => UtcHourFor(h, offset)).ToList();

        Assert.All(mapped, index => Assert.InRange(index, 0, 23));
        // A rotation is a permutation: every bucket used exactly once, nothing dropped or doubled.
        Assert.Equal(24, mapped.Distinct().Count());
    }
}
