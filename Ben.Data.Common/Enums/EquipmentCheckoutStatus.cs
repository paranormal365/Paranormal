namespace Ben.Data.Common.Enums;

/// <summary>
/// Where one equipment loan has got to.
/// </summary>
/// <remarks>
/// <para>One state machine for both flavours of loan. What differs is only <i>who approves</i>:
/// group-owned gear is reviewed by holders of the
/// <see cref="OrganizationSecurityTable.EquipmentCheckout"/> permission, while a member's personal
/// gear is always reviewed by its owner. The states, the dates and the notes are the same either
/// way, so there is one entity and one queue rather than two of everything.</para>
///
/// <para><see cref="Denied"/>, <see cref="Cancelled"/> and <see cref="Returned"/> are terminal.
/// There is deliberately no re-open: a fresh request is a new row, which keeps a loan's history
/// readable as a sequence of things that happened rather than one row that changed its mind.</para>
///
/// <para>Overdue is not a state. It is <see cref="CheckedOut"/> plus a due date in the past, and is
/// computed on read — storing it would mean a background job whose only purpose is to make a
/// comparison that is already free.</para>
/// </remarks>
public enum EquipmentCheckoutStatus
{
    /// <summary>Asked for, waiting on the approver.</summary>
    Requested = 1,

    /// <summary>Approved, but not yet in the borrower's hands.</summary>
    Approved = 2,

    /// <summary>Turned down, with a reason. Terminal.</summary>
    Denied = 3,

    /// <summary>Withdrawn by the borrower before they took it. Terminal.</summary>
    Cancelled = 4,

    /// <summary>The borrower has confirmed they have it.</summary>
    CheckedOut = 5,

    /// <summary>The lender has confirmed it came back. Terminal.</summary>
    Returned = 6,
}
