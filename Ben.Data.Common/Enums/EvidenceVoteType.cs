namespace Ben.Data.Common.Enums;

/// <summary>A voter's assessment of a piece of evidence.</summary>
/// <remarks>
/// The stored numbers are <b>not</b> the score. They are storage identifiers and are already in the
/// database on every vote ever cast; the score comes from <see cref="EvidenceVoteScore"/>. See its
/// remarks for why that separation is load-bearing rather than fussy.
/// </remarks>
public enum EvidenceVoteType
{
    Confirms     = 0,
    Disputes     = 1,
    Inconclusive = 2,
}

/// <summary>
/// Turns votes into a single signed number: <b>+1 confirms, 0 inconclusive, −1 disputes</b>.
/// </summary>
/// <remarks>
/// <para>Ben's ask: <i>"I want the indecisive to equal zero, then +1 for haunted and −1 for not
/// convinced."</i> Three separate counts are an accurate report and a poor summary — nothing in
/// them says whether a case leans haunted, and two cases with very different weights of opinion can
/// look alike.</para>
///
/// <para><b>The mapping is a function, never the enum's stored values.</b> Those are
/// <c>Confirms = 0, Disputes = 1, Inconclusive = 2</c>, and they sit in the database on every vote
/// already cast. Renumbering the enum to make the arithmetic fall out for free would silently
/// re-interpret every historical vote — a confirmation would become an inconclusive — with no
/// migration, no error, and nothing to notice it by. A test asserts the stored numbers have not
/// moved.</para>
///
/// <para>One place computes this, and every surface reuses it, for the reason
/// <c>PublicClientName</c> exists: four endpoints each doing their own arithmetic is four answers,
/// and the one that drifts is the one nobody is looking at.</para>
///
/// <para>The score is deliberately a <b>sum</b>, not an average, and it always travels with
/// <c>TotalVotes</c>. A sum is what Ben described, and the count beside it is what stops a score
/// being read without its weight — the same rule the equipment ratings follow. If an average is ever
/// wanted, it is this sum over that count and needs no new storage.</para>
/// </remarks>
public static class EvidenceVoteScore
{
    /// <summary>What one vote contributes: +1, 0 or −1.</summary>
    public static int Weight(EvidenceVoteType vote) => vote switch
    {
        EvidenceVoteType.Confirms     => 1,
        EvidenceVoteType.Disputes     => -1,
        EvidenceVoteType.Inconclusive => 0,
        // An unrecognised value contributes nothing rather than throwing: a new vote type should
        // not take down every page that reports a score before someone gets round to weighting it.
        _ => 0,
    };

    /// <summary>The signed total across a set of votes.</summary>
    public static int Score(IEnumerable<EvidenceVoteType> votes) => votes.Sum(Weight);

    /// <summary>
    /// The signed total from counts that have already been tallied, for callers that never hold the
    /// individual votes.
    /// </summary>
    public static int FromCounts(int confirms, int disputes, int inconclusive)
        => (confirms * Weight(EvidenceVoteType.Confirms))
         + (disputes * Weight(EvidenceVoteType.Disputes))
         + (inconclusive * Weight(EvidenceVoteType.Inconclusive));
}
