namespace Ben.Data.Common.Enums;

/// <summary>Whether a discount is one code everybody types, or a batch of unique ones.</summary>
public enum CouponKind
{
    /// <summary>
    /// One code, shared — printed on a flyer, said at a conference. Limited by a total redemption
    /// count if at all.
    /// </summary>
    Shared = 0,

    /// <summary>
    /// A generated batch of unique codes, each redeemable on its own terms — usually once.
    /// </summary>
    /// <remarks>
    /// The difference that matters is not how the string looks but <b>where the limit lives</b>: a
    /// shared code counts redemptions in one place, and a generated batch counts them per code, so
    /// one person burning their code cannot exhaust anybody else's.
    /// </remarks>
    Generated = 1,
}
