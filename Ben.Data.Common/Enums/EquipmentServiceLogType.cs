namespace Ben.Data.Common.Enums;

/// <summary>
/// What one entry in a piece of equipment's service log records.
/// </summary>
/// <remarks>
/// The log is the history; the matching fields on the item (<c>LastServicedDate</c>,
/// <c>DefectNotes</c>) are only ever a cache of its latest word, written in the same save as the
/// entry that changes them. Reading the current state from the item stays one column lookup, while
/// the log keeps the account of how it got there.
/// </remarks>
public enum EquipmentServiceLogType
{
    /// <summary>Serviced, calibrated, cleaned, batteries replaced — routine upkeep.</summary>
    Service = 1,

    /// <summary>Something is wrong with it. Also becomes the item's current defect note.</summary>
    DefectReported = 2,

    /// <summary>A previously reported defect has been dealt with. Clears the item's defect note.</summary>
    DefectResolved = 3,
}
