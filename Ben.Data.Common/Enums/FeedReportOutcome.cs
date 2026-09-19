namespace Ben.Data.Common.Enums;

/// <summary>
/// What an administrator decided about a reported feed post.
/// </summary>
/// <remarks>
/// Three states rather than a boolean, because "nobody has looked yet" and "somebody looked and
/// found nothing wrong" are different things to everyone involved: the reporter, the author, and
/// the next administrator opening the queue. A resolved-flag alone would conflate them.
/// </remarks>
public enum FeedReportOutcome
{
    /// <summary>Nobody has looked at it yet. The only state a report is created in.</summary>
    Pending   = 0,

    /// <summary>Looked at, and the post stays. The reporter was not agreed with.</summary>
    Dismissed = 1,

    /// <summary>Looked at, and the post was hidden.</summary>
    Hidden    = 2,
}
