namespace Ben.Data.Common.Enums;

/// <summary>
/// How a door knew who somebody was (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para>Recorded because these are different levels of certainty about who actually walked in,
/// and the difference is the whole of the conversation when an arrival is disputed afterwards. A
/// scanned code was in that person's hand; a name picked off a list was a steward's judgement in a
/// dark room.</para>
///
/// <para>Append-only, like every other enumeration stored as a number here.</para>
/// </remarks>
public enum HostedEventCheckInMethod
{
    /// <summary>The camera read their pass.</summary>
    Scanned = 0,

    /// <summary>Somebody typed the short code from the pass, because the camera would not.</summary>
    Typed = 1,

    /// <summary>Found by name on the door's own list of who is expected.</summary>
    ByName = 2,
}
