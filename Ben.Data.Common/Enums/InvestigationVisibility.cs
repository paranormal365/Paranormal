namespace Ben.Data.Common.Enums;

/// <summary>
/// Who may see an investigation's findings beyond the group that ran it.
/// </summary>
/// <remarks>
/// <para>Ordered by widening audience, and the default is chosen from the place rather than left to
/// whoever clicks fastest: somewhere a person lives starts at <see cref="GroupOnly"/>, a landmark
/// at <see cref="PlaceInvestigators"/>. Most investigations happen at people's homes, so the
/// cautious default is the common one.</para>
///
/// <para><b>Every read goes through one predicate</b> — <c>InvestigationVisibilityFilter</c> — so
/// that the rules below live in a single place and a future change to them is a single edit rather
/// than an audit of every query.</para>
/// </remarks>
public enum InvestigationVisibility
{
    /// <summary>
    /// Only the organization that ran it. The safe default, and where anything at somebody's home
    /// stays unless a person deliberately widens it.
    /// </summary>
    GroupOnly = 1,

    /// <summary>
    /// Anyone whose organization has also investigated this place.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately <b>not reciprocal</b>: you do not have to publish your own findings to
    /// read everyone else's. Requiring a contribution would mostly deter the cautious rather than
    /// the freeloading, and the point of the scope is that people comparing notes on the same
    /// building is useful. Revisit if lurking turns out to be a real problem — the predicate is one
    /// function, so it is one change.</para>
    ///
    /// <para>The membership is <i>dynamic</i>, and that is the part worth saying out loud in the
    /// UI: an organization that investigates the place next year gains access to what was shared
    /// this year. Choosing this scope is a decision about the future as much as the present.</para>
    /// </remarks>
    PlaceInvestigators = 2,

    /// <summary>
    /// Anybody, including visitors who are not signed in.
    /// </summary>
    /// <remarks>
    /// Refused on a private residence for now. Publishing what happened inside someone's home is
    /// theirs to agree to, and there is no mechanism yet for asking them — see the branch README's
    /// open question on client consent. Better to withhold the option than to offer one that
    /// quietly skips the consent.
    /// </remarks>
    Public = 3,
}
