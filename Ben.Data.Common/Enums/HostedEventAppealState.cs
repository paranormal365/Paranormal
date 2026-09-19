namespace Ben.Data.Common.Enums;

/// <summary>
/// Where an organizer's appeal against a removed event stands (item 235 phase 17b). Append only; the numbers are stored.
/// </summary>
public enum HostedEventAppealState
{
    /// <summary>Removed, and the organizer has not appealed.</summary>
    NotAppealed = 0,

    /// <summary>Appealed, and waiting for somebody at IsHaunted to answer.</summary>
    Waiting = 1,

    /// <summary>The appeal was upheld and the event came back as a draft.</summary>
    Upheld = 2,

    /// <summary>The appeal was declined. The event stays removed.</summary>
    Declined = 3,
}
