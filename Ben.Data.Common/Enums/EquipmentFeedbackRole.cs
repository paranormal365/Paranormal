namespace Ben.Data.Common.Enums
{
    /// <summary>Which side of a loan wrote a piece of feedback.</summary>
    /// <remarks>
    /// Both directions exist, and they are not symmetrical in attribution. Comments a lender leaves
    /// about a borrower are shown to future lenders <b>with the lender's name</b> — that is
    /// lender-to-lender context, and an unattributed warning is hard to weigh. Comments a borrower
    /// leaves about a lender are shown <b>unattributed</b>: borrowers have less standing and more to
    /// lose, so the protection goes where it is needed.
    /// </remarks>
    public enum EquipmentFeedbackRole
    {
        /// <summary>Written by whoever lent the gear, about the borrower.</summary>
        Lender = 0,

        /// <summary>Written by whoever borrowed it, about the lender — and optionally about the gear.</summary>
        Borrower = 1,
    }
}
