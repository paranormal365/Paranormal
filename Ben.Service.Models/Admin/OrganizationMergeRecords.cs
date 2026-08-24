namespace Ben.Service.Models.Admin;

/// <summary>What a merge would do — shown to the admin BEFORE anything is written (item 110).</summary>
public sealed record MergePreview(
    string BaseName,
    string MergedName,
    IReadOnlyList<MergeTableCount> Reparented,
    IReadOnlyList<string> Notes);

public sealed record MergeTableCount(string Table, int Rows);

public sealed record OrganizationMergeRequest(
    Guid BaseOrganizationId, Guid MergedOrganizationId, string? NewName);
