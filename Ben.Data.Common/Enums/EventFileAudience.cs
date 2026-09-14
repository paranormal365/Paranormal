namespace Ben.Data.Common.Enums;

/// <summary>Who may read a file kept with a hosted event (item 235).</summary>
/// <remarks>
/// Three audiences and no fourth: the people running it, the people attending it, and anybody at
/// all. The default on upload is <see cref="Staff"/>, because a running order and a floor plan are
/// not the same kind of thing as a welcome pack, and the safe mistake is the one that shows a file
/// to too few people.
/// </remarks>
public enum EventFileAudience
{
    /// <summary>The event's staff only.</summary>
    Staff = 0,

    /// <summary>Staff and anybody with a confirmed booking.</summary>
    Attendees = 1,

    /// <summary>Anybody, including visitors who are not signed in.</summary>
    Public = 2,
}
