namespace Ben.Data.Common.Enums
{
    /// <summary>Where a question asked about a piece of equipment has got to.</summary>
    public enum EquipmentQuestionStatus
    {
        /// <summary>Asked, not yet answered.</summary>
        Open = 0,

        /// <summary>Answered, and the asker has been told.</summary>
        Answered = 1,

        /// <summary>
        /// Closed without an answer. Deliberately distinct from Answered rather than a deletion:
        /// the asker is told either way, and an owner who would rather not answer should not have
        /// to leave a question hanging to avoid answering it.
        /// </summary>
        Declined = 2,
    }
}
