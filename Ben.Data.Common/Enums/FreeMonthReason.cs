namespace Ben.Data.Common.Enums;

/// <summary>Why a group was given free months on its plan (11/02/2026).</summary>
public enum FreeMonthReason
{
    /// <summary>A group it referred made its first real payment.</summary>
    Referral = 0,

    /// <summary>The best per-member score in a calendar month of the founding groups promotion.</summary>
    GroupOfTheMonth = 1,

    /// <summary>Reached the promotion's score over its own twelve months.</summary>
    ReachedTheScore = 2,

    /// <summary>One of the promotion's top three: a second year free.</summary>
    SecondYear = 3,

    /// <summary>Given by an administrator.</summary>
    Given = 4,
}
