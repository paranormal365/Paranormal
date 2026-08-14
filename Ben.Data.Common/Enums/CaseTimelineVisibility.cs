namespace Ben.Data.Common.Enums;

/// <summary>
/// Who can see a case timeline entry.
/// </summary>
/// <remarks>
/// <para>Cumulative: each level is visible to that audience <i>and</i> everyone closer to the case.
/// Active org members always see every entry regardless — an investigator can't do the work while
/// blind to half the file.</para>
///
/// <para>Replaces a binary <c>IsPublic</c>, which could only say "on the public page or not" and so
/// gave the client and the general public identical access. That left no way to tell a client
/// something without also telling the internet, which is why clients previously saw only their own
/// reports.</para>
///
/// <para><see cref="OrgOnly"/> is 0 so the backfill of the old <c>false</c> — the overwhelming
/// majority of entries, and the safer reading of "not public" — needs no data change.</para>
/// </remarks>
public enum CaseTimelineVisibility
{
    /// <summary>Investigators only. The default for working notes.</summary>
    OrgOnly = 0,

    /// <summary>The org and the case's client(s). Not on the public page.</summary>
    Client = 1,

    /// <summary>Anyone, including the public case page.</summary>
    Public = 2,
}
