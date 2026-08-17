using Ben.Data.Common.Enums;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The signed haunting score (backlog item #81): +1 confirms, 0 inconclusive, −1 disputes.
/// </summary>
/// <remarks>
/// The arithmetic is trivial and barely worth a test. What is worth a test is the <b>trap</b> the
/// backlog entry called out before anyone wrote a line: the enum's stored values are
/// <c>Confirms = 0, Disputes = 1, Inconclusive = 2</c>, already on every vote in the database.
/// Renumbering them so the weights fell out for free would silently re-interpret history — every
/// confirmation becoming an inconclusive — with no migration and nothing to notice it by.
/// </remarks>
public sealed class EvidenceVoteScoreTests
{
    /// <summary>
    /// Pins the stored numbers. If this fails, the change under review is rewriting the meaning of
    /// votes already cast, whatever it looks like it is doing.
    /// </summary>
    [Fact]
    public void The_stored_enum_values_have_not_moved()
    {
        Assert.Equal(0, (int)EvidenceVoteType.Confirms);
        Assert.Equal(1, (int)EvidenceVoteType.Disputes);
        Assert.Equal(2, (int)EvidenceVoteType.Inconclusive);
    }

    /// <summary>
    /// And the score is a mapping, not a cast — which is the same statement from the other side.
    /// Every weight differs from its own stored value, so an implementation that quietly used the
    /// stored number could not pass this.
    /// </summary>
    [Theory]
    [InlineData(EvidenceVoteType.Confirms, 1)]
    [InlineData(EvidenceVoteType.Disputes, -1)]
    [InlineData(EvidenceVoteType.Inconclusive, 0)]
    public void Each_vote_weighs_what_Ben_asked_for(EvidenceVoteType vote, int expected)
    {
        Assert.Equal(expected, EvidenceVoteScore.Weight(vote));
        Assert.NotEqual((int)vote, EvidenceVoteScore.Weight(vote));
    }

    [Fact]
    public void An_inconclusive_vote_pulls_the_score_towards_neither_side()
    {
        EvidenceVoteType[] withoutIt = [EvidenceVoteType.Confirms, EvidenceVoteType.Confirms];
        EvidenceVoteType[] withIt = [.. withoutIt, EvidenceVoteType.Inconclusive];

        // It counts as a vote — TotalVotes goes up — but moves the score nowhere. That is the whole
        // point of casting one.
        Assert.Equal(EvidenceVoteScore.Score(withoutIt), EvidenceVoteScore.Score(withIt));
    }

    [Fact]
    public void Opposed_votes_cancel_to_zero_rather_than_to_nothing()
    {
        EvidenceVoteType[] split = [EvidenceVoteType.Confirms, EvidenceVoteType.Disputes];

        // Zero from a real disagreement and zero from an empty case are the same number, which is
        // why every surface carries TotalVotes beside it.
        Assert.Equal(0, EvidenceVoteScore.Score(split));
        Assert.Equal(0, EvidenceVoteScore.Score([]));
    }

    [Fact]
    public void A_case_can_lean_either_way()
    {
        Assert.Equal(2, EvidenceVoteScore.Score(
            [EvidenceVoteType.Confirms, EvidenceVoteType.Confirms, EvidenceVoteType.Confirms,
             EvidenceVoteType.Disputes, EvidenceVoteType.Inconclusive]));

        Assert.Equal(-2, EvidenceVoteScore.Score(
            [EvidenceVoteType.Disputes, EvidenceVoteType.Disputes]));
    }

    /// <summary>
    /// The pre-tallied path must agree with the vote-by-vote one — the discovery list uses the
    /// former and the case page the latter, and two answers for one case is the exact failure the
    /// single-source rule exists to prevent.
    /// </summary>
    [Fact]
    public void Counting_the_votes_and_scoring_the_counts_agree()
    {
        EvidenceVoteType[] votes =
        [
            EvidenceVoteType.Confirms, EvidenceVoteType.Confirms, EvidenceVoteType.Confirms,
            EvidenceVoteType.Disputes,
            EvidenceVoteType.Inconclusive, EvidenceVoteType.Inconclusive,
        ];

        Assert.Equal(
            EvidenceVoteScore.Score(votes),
            EvidenceVoteScore.FromCounts(confirms: 3, disputes: 1, inconclusive: 2));
    }

    /// <summary>
    /// An unrecognised value contributes nothing instead of throwing. A fourth vote type added
    /// later should show up as unweighted, not take down every page that reports a score.
    /// </summary>
    [Fact]
    public void An_unknown_vote_type_is_ignored_rather_than_fatal()
    {
        Assert.Equal(0, EvidenceVoteScore.Weight((EvidenceVoteType)99));
    }
}
