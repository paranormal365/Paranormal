namespace Ben.Data.Common.Enums;

/// <summary>
/// What a comparison scan found about one thing in a room (LiDAR plan, phase 2).
/// </summary>
/// <remarks>Append only; the numbers are stored.</remarks>
public enum PlaceScanChangeKind : byte
{
    /// <summary>The same thing, more than 3 cm or 5° from where the room's scan has it.</summary>
    Moved = 1,

    /// <summary>In the room's scan, and not there on the later visit.</summary>
    Missing = 2,

    /// <summary>There on the later visit, and not in the room's scan.</summary>
    New = 3,
}
