namespace Ben.Data.WebApi.Services.Feed;

/// <summary>What the ranking needs to know about one post. Deliberately not the entity.</summary>
/// <param name="Id">The post.</param>
/// <param name="DateCreated">When it was written, UTC.</param>
/// <param name="Likes">How many people liked it.</param>
/// <param name="Replies">How many visible replies it drew.</param>
/// <param name="MatchScore">The category-match score (item 186 F6), when the post has one. Null
/// leaves ranking untouched — text posts and unscored media are neither rewarded nor punished.</param>
public readonly record struct RankableFeedPost(
    Guid Id, DateTime DateCreated, int Likes, int Replies, double? MatchScore = null);

/// <summary>
/// The "For You" ordering (item 186 F3): fresh things surface, engaging things stay up, and
/// everything sinks eventually.
/// </summary>
/// <remarks>
/// <para><b>Score = (1 + 4·likes + 2·replies) / (ageHours + 2)^1.5.</b> The classic gravity shape,
/// and the reasons for each piece:</para>
///
/// <para>The <b>+1</b> is what lets a brand-new post with no engagement appear at all. Without it
/// a post starts at zero and can only be found by somebody reading Latest — which is how a feed
/// ends up showing the same popular week forever and nobody's first post is ever seen.</para>
///
/// <para><b>Likes count double replies</b> (4 vs 2) because a like is cheap and a reply is not:
/// weighting them equally would let one argument outrank a genuinely liked piece of evidence.
/// Both are tunable constants; neither is load-bearing on anything but taste.</para>
///
/// <para>The <b>+2 hours</b> in the denominator stops the first minutes of a post's life from
/// being a cliff — without it, age 0 divides by nearly nothing and a single like on a
/// one-minute-old post outranks everything else on the site.</para>
///
/// <para><b>Exponent 1.5</b> is the decay rate: gentler than 2 (which buries a day-old post),
/// sharper than 1 (which lets a popular post sit at the top for a week).</para>
///
/// <para><b>Ties break by recency, then by id</b> — deterministic, because a ranking that shuffles
/// equal-scoring posts between requests would break the cursor: page two would re-show what page
/// one already did, and the reader would think the feed was repeating itself.</para>
///
/// <para><b>Pure and static</b> so the rule can be tested with no database at all: the interesting
/// question is "does a liked old post beat an unliked new one", and that deserves an answer that
/// does not depend on EF.</para>
/// </remarks>
public static class FeedRanking
{
    /// <summary>Weight of one like. Tunable; see the class remarks.</summary>
    public const double LikeWeight = 4.0;

    /// <summary>Weight of one reply.</summary>
    public const double ReplyWeight = 2.0;

    /// <summary>Hours added to age, so a brand-new post is not divided by nearly zero.</summary>
    public const double AgeCushionHours = 2.0;

    /// <summary>How sharply a post sinks with age.</summary>
    public const double Gravity = 1.5;

    /// <summary>
    /// How much a category mismatch can cost (item 186 F6): the multiplier runs from
    /// <c>MatchFloor</c> (score 0) to 1.0 (score 1). A signal, not a verdict — a certainly
    /// mislabelled post ranks like one with a quarter fewer eyes on it, it does not vanish.
    /// </summary>
    public const double MatchFloor = 0.75;

    /// <summary>One post's score. Higher is better. Never negative.</summary>
    public static double Score(RankableFeedPost post, DateTime nowUtc)
    {
        var ageHours = Math.Max(0, (nowUtc - post.DateCreated).TotalHours);
        var engagement = 1 + (LikeWeight * post.Likes) + (ReplyWeight * post.Replies);
        var match = post.MatchScore is { } score
            ? MatchFloor + ((1 - MatchFloor) * Math.Clamp(score, 0, 1))
            : 1.0;
        return match * engagement / Math.Pow(ageHours + AgeCushionHours, Gravity);
    }

    /// <summary>
    /// The candidate window in ranked order, best first.
    /// </summary>
    /// <remarks>
    /// Ordering happens here rather than in SQL because the score is not something a database
    /// index can help with — every row in the window has to be scored either way — and because
    /// keeping it in one pure function is what makes the rule reviewable.
    /// </remarks>
    public static IReadOnlyList<RankableFeedPost> Rank(
        IEnumerable<RankableFeedPost> candidates, DateTime nowUtc)
        => [.. candidates
            .OrderByDescending(p => Score(p, nowUtc))
            .ThenByDescending(p => p.DateCreated)
            .ThenByDescending(p => p.Id)];
}
