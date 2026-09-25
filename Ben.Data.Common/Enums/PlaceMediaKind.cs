namespace Ben.Data.Common.Enums;

/// <summary>
/// What a file attached to a place IS — evidence, or a picture of the building (item 250).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-21: <i>"images of it — not evidence but the property and photos inside to
/// show it."</i></para>
///
/// <para><b>The distinction is the whole design.</b> A photograph of a staircase taken in daylight
/// to show a reader what the house looks like, and a photograph of the same staircase somebody
/// thinks has a figure on it, are different claims. Mixed into one gallery the page is useless:
/// every count is wrong, every vote is on something that was never offered as evidence, and a
/// reader cannot tell what anybody is asserting.</para>
///
/// <para><b>Why a kind rather than two tables.</b> Both need the same screening, the same
/// place-kind rule and the same serving door, and two copies of those would drift — the failure
/// this codebase keeps meeting. What stops the two being confused instead is that every query for
/// them takes the kind as a REQUIRED argument, so there is no call that returns both by accident.
/// </para>
/// </remarks>
public enum PlaceMediaKind
{
    /// <summary>
    /// Offered because of what is in it. Voted on, counted, charted.
    /// </summary>
    /// <remarks>
    /// Zero, so every row written before this enum existed — all of which were evidence — reads
    /// correctly with no migration of data and nothing to get wrong.
    /// </remarks>
    Evidence = 0,

    /// <summary>
    /// A picture of the place itself. Never voted on and never counted as evidence.
    /// </summary>
    /// <remarks>
    /// The grounds, the rooms, the frontage — what makes the page worth reading for somebody who
    /// has never been. It is context, and counting it as evidence would inflate every figure on
    /// the page with photographs nobody claimed anything about.
    /// </remarks>
    AboutThePlace = 1,
}
