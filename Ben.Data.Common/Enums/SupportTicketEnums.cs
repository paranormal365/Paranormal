namespace Ben.Data.Common.Enums;

/// <summary>What a visitor is writing in about.</summary>
/// <remarks>
/// Numeric values are the wire contract — nothing in this solution registers a
/// <c>JsonStringEnumConverter</c>, so these cross as integers and must not be renumbered.
/// </remarks>
public enum SupportTicketTopic
{
    /// <summary>Trouble using the site — sign-in, a page not working, how do I do X.</summary>
    WebsiteHelp = 0,

    /// <summary>Something is broken or wrong.</summary>
    ReportProblem = 1,

    /// <summary>Wants to reach a person rather than solve a problem.</summary>
    ContactStaff = 2,

    /// <summary>Anything else.</summary>
    Other = 3,
}

/// <summary>Where a ticket stands.</summary>
public enum SupportTicketStatus
{
    /// <summary>Submitted, nobody has picked it up.</summary>
    New = 0,

    /// <summary>Someone is working on it.</summary>
    Open = 1,

    /// <summary>Staff have replied and are waiting on the sender.</summary>
    Answered = 2,

    /// <summary>Done. Closed tickets are kept, never deleted.</summary>
    Closed = 3,
}
