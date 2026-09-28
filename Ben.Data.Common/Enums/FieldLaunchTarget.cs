namespace Ben.Data.Common.Enums;

/// <summary>What a lead started everybody's Field Kit for (item 252).</summary>
public enum FieldLaunchTarget
{
    /// <summary>An investigation — on its own, one visit in a case, or at a public event.</summary>
    Investigation = 0,

    /// <summary>A calendar event: a tour date, or a public event on the group's calendar.</summary>
    CalendarEvent = 1,

    /// <summary>A hosted event — a weekend at a venue, a dinner and a show.</summary>
    HostedEvent = 2,
}

/// <summary>Where somebody's request to join a launch stands (item 252).</summary>
public enum FieldLaunchJoinStatus
{
    Pending = 0,
    Approved = 1,
    Declined = 2,
}
