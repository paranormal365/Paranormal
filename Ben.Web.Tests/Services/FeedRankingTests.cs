using Ben.Data.WebApi.Services.Feed;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The "For You" ordering (item 186 F3): what actually rises, and what that costs.
/// </summary>
/// <remarks>
/// These are the claims the ranking makes about itself, written as tests because the constants are
/// tunable and somebody will tune them. Each one names the behaviour that must survive the tuning,
/// not the arithmetic — a test asserting a score equals 0.42 would fail on every adjustment while
/// telling nobody what broke.
/// </remarks>
public sealed class FeedRankingTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static RankableFeedPost Post(double ageHours, int likes = 0, int replies = 0)
        => new(Guid.NewGuid(), Now.AddHours(-ageHours), likes, replies);

    [Fact]
    public void With_equal_engagement_the_fresher_post_wins()
    {
        var fresh = Post(ageHours: 1);
        var stale = Post(ageHours: 48);

        Assert.True(FeedRanking.Score(fresh, Now) > FeedRanking.Score(stale, Now));
    }

    [Fact]
    public void Engagement_lifts_an_older_post_above_a_newer_silent_one()
    {
        // The whole reason For You exists: a night's evidence that people are actually discussing
        // should outrank a two-hour-old "testing" post nobody touched.
        var likedYesterday = Post(ageHours: 20, likes: 12, replies: 4);
        var silentAndNew = Post(ageHours: 2);

        Assert.True(FeedRanking.Score(likedYesterday, Now) > FeedRanking.Score(silentAndNew, Now));
    }

    [Fact]
    public void A_normally_liked_post_sinks_below_fresh_content_within_days()
    {
        // The turnover that matters in practice: yesterday's well-received post is still up, and
        // last week's is not. Written with ordinary numbers because those are the ones the feed
        // will actually see.
        var yesterday = Post(ageHours: 24, likes: 20);
        var lastWeek = Post(ageHours: 24 * 7, likes: 20);
        var freshAndSilent = Post(ageHours: 3);

        Assert.True(FeedRanking.Score(yesterday, Now) > FeedRanking.Score(freshAndSilent, Now));
        Assert.True(FeedRanking.Score(freshAndSilent, Now) > FeedRanking.Score(lastWeek, Now));
    }

    /// <summary>
    /// The candidate window, not gravity, is the hard backstop on age.
    /// </summary>
    /// <remarks>
    /// Worth stating because the intuition is wrong and this test was written asserting the
    /// intuition first: a genuinely viral post (500 likes) still outscores a brand-new silent one
    /// a MONTH later — 2401/722^1.5 beats 1/5^1.5. Gravity slows a runaway post; it does not
    /// retire it. What retires it is <c>RankingWindowDays</c> in FeedController, which never
    /// offers it as a candidate at all. If that window is ever widened, this is the arithmetic
    /// that decides whether a hall of fame appears at the top of everyone's feed.
    /// </remarks>
    [Fact]
    public void Gravity_alone_does_not_retire_a_runaway_post_the_window_does()
    {
        var viralLastMonth = Post(ageHours: 24 * 30, likes: 500, replies: 200);
        var freshAndSilent = Post(ageHours: 3);

        Assert.True(FeedRanking.Score(viralLastMonth, Now) > FeedRanking.Score(freshAndSilent, Now));
    }

    [Fact]
    public void A_brand_new_post_with_nothing_on_it_still_scores_above_zero()
    {
        // The +1: without it a first post is invisible in For You forever, and a feed where
        // newcomers are never seen stops acquiring newcomers.
        Assert.True(FeedRanking.Score(Post(ageHours: 0), Now) > 0);
    }

    [Fact]
    public void A_like_counts_for_more_than_a_reply()
    {
        var liked = Post(ageHours: 5, likes: 1);
        var replied = Post(ageHours: 5, replies: 1);

        Assert.True(FeedRanking.Score(liked, Now) > FeedRanking.Score(replied, Now));
    }

    [Fact]
    public void A_post_from_the_future_is_not_divided_by_a_negative_age()
    {
        // Clock skew between the web host and the database is not hypothetical, and a negative
        // age raised to a fractional power is NaN — which sorts unpredictably and would scatter
        // the whole page.
        var future = Post(ageHours: -6, likes: 1);

        var score = FeedRanking.Score(future, Now);
        Assert.False(double.IsNaN(score));
        Assert.True(score > 0);
    }

    [Fact]
    public void Ties_break_deterministically_so_paging_cannot_repeat_itself()
    {
        // Two posts, same instant, same engagement. The order must be the same on every call, or
        // page two re-shows what page one already did.
        var earlier = new RankableFeedPost(
            new Guid("11111111-1111-1111-1111-111111111111"), Now.AddHours(-4), 2, 0);
        var later = new RankableFeedPost(
            new Guid("22222222-2222-2222-2222-222222222222"), Now.AddHours(-4), 2, 0);

        var first = FeedRanking.Rank([earlier, later], Now).Select(p => p.Id).ToList();
        var second = FeedRanking.Rank([later, earlier], Now).Select(p => p.Id).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Rank_orders_best_first_and_keeps_every_candidate()
    {
        var best = Post(ageHours: 1, likes: 10);
        var middle = Post(ageHours: 1, likes: 3);
        var worst = Post(ageHours: 200);

        var ranked = FeedRanking.Rank([worst, best, middle], Now);

        Assert.Equal([best.Id, middle.Id, worst.Id], ranked.Select(p => p.Id));
    }
}
