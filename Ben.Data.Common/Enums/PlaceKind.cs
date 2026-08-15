namespace Ben.Data.Common.Enums;

/// <summary>
/// What kind of location a <c>Place</c> is, which decides how freely findings about it may be
/// shared by default.
/// </summary>
/// <remarks>
/// <para>The distinction is about consent, not about architecture. Somebody lives at a private
/// residence and did not volunteer their home to an audience; a landmark that runs public ghost
/// tours is in a different position entirely. So the two defaults differ, and they differ in the
/// safe direction — see the visibility work in P6.</para>
///
/// <para>Deliberately only two values. "Commercial", "cemetery", "abandoned" and the rest are
/// descriptions of a place, not decisions about who may see what, and the moment this enum starts
/// carrying descriptions it stops being usable as the sharing default it exists to be.</para>
/// </remarks>
public enum PlaceKind
{
    /// <summary>Somewhere a person lives. Findings stay with the group unless someone says otherwise.</summary>
    PrivateResidence = 1,

    /// <summary>A landmark, business, or other location that is not somebody's home.</summary>
    PublicLocation = 2,
}
