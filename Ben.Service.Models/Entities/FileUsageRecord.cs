namespace Ben.Service.Models.Entities;

/// <summary>
/// Where a person's file is in use beyond their own library (item 180 Phase B) — what the two
/// delete questions are asked about.
/// </summary>
/// <param name="PersonShares">Active shares to individual people.</param>
/// <param name="IsPublic">The file itself is public, or an active public share exists.</param>
/// <param name="Organizations">Every group with a claim on it, one row each.</param>
public sealed record FileUsageRecord(
    Guid UploadFileId,
    string FileName,
    int PersonShares,
    bool IsPublic,
    IReadOnlyList<FileUsageOrganizationRecord> Organizations)
{
    /// <summary>True when at least one group is using it — the case that asks the questions.</summary>
    public bool InUseByAnOrganization => Organizations.Count > 0;
}

/// <summary>
/// One group's claim on a file. Counts rather than lists: the person choosing sees the size of
/// what they are about to remove, and the group's case titles are not theirs to read.
/// </summary>
/// <param name="Shares">Active shares to this group (either share table).</param>
/// <param name="CaseCopies">Copies attached to this group's cases.</param>
/// <param name="GroupCopies">Copies the group made into its own Files.</param>
/// <param name="DirectLinks">Places the original itself is referenced: a case, a timeline entry, a
/// report, the group's logo or an ad, an event's evidence, an equipment photo.</param>
public sealed record FileUsageOrganizationRecord(
    Guid OrganizationId,
    string OrganizationName,
    int Shares,
    int CaseCopies,
    int GroupCopies,
    int DirectLinks)
{
    public int Total => Shares + CaseCopies + GroupCopies + DirectLinks;
}

/// <summary>Hand the file to this group instead of destroying it.</summary>
public sealed record ReassignUploadFileRequest(Guid OrganizationId);

/// <summary>What happened to a file the person chose to remove everywhere.</summary>
public sealed record DeleteEverywhereResult(
    int SharesRemoved, int CaseCopiesRemoved, int GroupCopiesRemoved, int DirectLinksRemoved);
