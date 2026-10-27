namespace Ben.Data.Common.Enums;

/// <summary>
/// Whose copy of a field session's recording a scan works on (phase 9, 10/27/2026).
/// </summary>
/// <remarks>
/// Each scanner works on their own copy with their own markers, and nobody sees anybody else's.
/// A group's copy belongs to the group: its active members and the event's confirmed staff see it.
/// </remarks>
public enum AnalysisScope
{
    /// <summary>The person's own copy, in their own files.</summary>
    Person = 0,

    /// <summary>A group's copy, in the group's files.</summary>
    Organization = 1,
}
