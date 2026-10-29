namespace Ben.Data.Common.Enums;

/// <summary>
/// Where one version of a room's scan stands (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// <para>A place keeps one current scan per room. Everything else a room has ever had is a version
/// in one of the other states, kept so a comparison made against it still resolves.</para>
///
/// <para>Append only. The numbers are stored, and the unique index on the current scan filters on
/// <see cref="Current"/> by number.</para>
/// </remarks>
public enum PlaceScanState : byte
{
    /// <summary>The room's scan. One per room per place.</summary>
    Current = 1,

    /// <summary>Replaced by a newer scan. Kept, because comparisons were made against it.</summary>
    Previous = 2,

    /// <summary>
    /// Sent by somebody who may not replace the current scan, for the custodian to accept or
    /// decline. Shown to nobody else meanwhile.
    /// </summary>
    Offered = 3,

    /// <summary>An offer the custodian turned down. Its file is gone; the row says what happened.</summary>
    Declined = 4,

    /// <summary>Taken down by the custodian. Its file is gone; comparisons keep their rows.</summary>
    Removed = 5,
}
