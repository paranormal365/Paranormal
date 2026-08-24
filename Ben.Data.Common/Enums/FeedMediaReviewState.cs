namespace Ben.Data.Common.Enums;

/// <summary>
/// Whether a feed post's photo or video may be shown to anybody (item 186 F4/F5).
/// </summary>
/// <remarks>
/// <para><b>The default is Pending, and Pending is never served.</b> That ordering is the whole
/// safety design: media is fail-closed by the data model rather than by a service remembering to
/// check. Ben's requirement was plain — "I don't want a website full of porn" — and the way to
/// honour it is to make unscreened media structurally unservable, so that forgetting to screen
/// shows up as a photo that will not display rather than as a photo nobody should have seen.</para>
///
/// <para>F4 ships the states and the serving rule. F5 ships the screening that moves media out of
/// Pending automatically, plus the queue where a moderator reviews whatever is Held. Between the
/// two, media can be posted and simply does not render — which is the correct behaviour for a
/// site whose screening does not exist yet, and is why this shipped in this order.</para>
///
/// <para>Append-only, like every other enum here: the numbers are in the database.</para>
/// </remarks>
public enum FeedMediaReviewState
{
    /// <summary>Not yet screened. Never served, to anybody, including the author.</summary>
    Pending = 0,

    /// <summary>Screened and allowed. The only state that renders publicly.</summary>
    Approved = 1,

    /// <summary>Screening or a moderator refused it. Kept, not deleted — see the review queue.</summary>
    Held = 2,
}
