namespace Ben.Service.Models.Entities;

/// <summary>
/// Preview of what replacing an UploadFile's bytes will touch (item #6 phase 3) — every case that
/// holds a copy of the file, so the owner can see the blast radius before confirming.
/// </summary>
public record ReplaceImpactRecord(
    Guid UploadFileId,
    string FileName,
    IReadOnlyList<ReplaceImpactCaseRecord> Cases);

/// <summary>One case's copy of the file being replaced, with its existing comment/vote counts.</summary>
public record ReplaceImpactCaseRecord(
    Guid CaseId,
    string CaseTitle,
    string OrganizationName,
    Guid CopyUploadFileId,
    int CommentCount,
    int VoteCount);
