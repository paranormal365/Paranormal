namespace Ben.Data.Common.Enums;

/// <summary>What a hosted event's findings put on its location's page (Ben, 10/25/2026).</summary>
public enum FindingsOnPlace
{
    /// <summary>Nothing: the findings stay on the event.</summary>
    None = 0,

    /// <summary>The pieces the organizer chose.</summary>
    Chosen = 1,

    /// <summary>Every public finding from the event.</summary>
    AllPublic = 2,
}
