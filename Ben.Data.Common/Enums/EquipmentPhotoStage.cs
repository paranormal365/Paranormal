namespace Ben.Data.Common.Enums;

/// <summary>
/// Which end of a loan a condition photo was taken at.
/// </summary>
/// <remarks>
/// The whole point of recording the stage is the comparison: a piece photographed as it went out
/// and again as it came back answers "was it already like that?" without anyone having to
/// remember. Kept as a stage on the photo rather than two separate collections so the loan detail
/// can lay them side by side from one query.
/// </remarks>
public enum EquipmentPhotoStage
{
    /// <summary>Taken as the gear went out — the "before".</summary>
    Handoff = 1,

    /// <summary>Taken as the gear came back — the "after".</summary>
    Return = 2,
}
