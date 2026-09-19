using Ben.Data.Common.Enums;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// When a case's "Closed" date should be set, cleared, or left exactly where it is.
/// </summary>
/// <remarks>
/// <para><b>It used to be write-once, and said so on the screen.</b> The rule set the date on
/// Closed/Public/Haunted and had no other branch, so a case reopened to Active kept it — Ben's
/// own case #2026-002 sat under a green "Active" badge with "Closed 09/19/2026" beside it
/// (2026-09-19). A date nobody can clear is a date that stops being true.</para>
///
/// <para><b>Symmetric, and deliberately not total.</b> The closing statuses set it and the working
/// ones clear it, but <see cref="CaseStatus.Transferred"/> and <see cref="CaseStatus.Paused"/>
/// touch neither — handing a case to another group is not reopening it, and a lapsed subscription
/// restores its old status from <c>Case.StatusBeforePause</c> when the group renews, so clearing
/// the date on the way into Paused would lose the closed date of a case that was published before
/// the lapse and hand it back wrong.</para>
///
/// <para>"Working" is <c>&lt;= Summarized</c>, which is how the rest of the codebase already
/// counts a case as open — see the remarks on <see cref="CaseStatus.Paused"/> for why that
/// comparison is safe to rely on and why statuses are appended rather than renumbered.</para>
///
/// <para>Pure, so the rule can be read and tested on its own rather than inferred from one line
/// in the middle of an update action.</para>
/// </remarks>
public static class CaseClosureDate
{
    /// <summary>Statuses that mean the work is over.</summary>
    public static bool IsClosing(CaseStatus status)
        => status is CaseStatus.Closed or CaseStatus.Public or CaseStatus.Haunted;

    /// <summary>Statuses that mean the work is still going.</summary>
    public static bool IsWorking(CaseStatus status)
        => status <= CaseStatus.Summarized;

    /// <summary>
    /// The closed date a case should carry after moving to <paramref name="status"/>.
    /// </summary>
    /// <param name="existing">What it carries now.</param>
    /// <param name="nowUtc">Passed in rather than read, so the rule has no clock of its own.</param>
    /// <returns>
    /// The first closing date it was given, null once it is being worked again, and
    /// <paramref name="existing"/> unchanged for the statuses that are neither.
    /// </returns>
    public static DateTime? After(CaseStatus status, DateTime? existing, DateTime nowUtc)
    {
        if (IsClosing(status)) return existing ?? nowUtc;
        if (IsWorking(status)) return null;
        return existing;
    }
}
