namespace Ben.Data.Common.Enums;

/// <summary>
/// Where one request for more time on a loan has got to.
/// </summary>
/// <remarks>
/// A renewal is a child of the loan rather than a state of it: the loan stays
/// <see cref="EquipmentCheckoutStatus.CheckedOut"/> throughout, because the gear never changed
/// hands. Modelling it as a separate row also keeps a history of what was asked and answered,
/// which a mutated due date on the loan itself would erase.
/// </remarks>
public enum EquipmentRenewalStatus
{
    /// <summary>Asked for, waiting on whoever lent the gear.</summary>
    Requested = 1,

    /// <summary>Granted — the loan's due date moves to the requested one.</summary>
    Approved = 2,

    /// <summary>Turned down, with a reason. The original due date stands.</summary>
    Denied = 3,
}
