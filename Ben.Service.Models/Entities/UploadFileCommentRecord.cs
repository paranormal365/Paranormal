namespace Ben.Service.Models.Entities;

/// <summary>Read-only projection of a comment posted on an UploadFile.</summary>
public record UploadFileCommentRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public Guid AuthorAppUserId { get; init; }
    public string? AuthorDisplayName { get; init; }
    public required string Text { get; init; }

    // ── Audience snapshot, frozen at post time — see UploadFileComment's doc comment. ──
    public bool IsOwner { get; init; }
    public bool IsInvestigationTeamMember { get; init; }
    public bool IsClient { get; init; }
    public bool IsOrganizationMember { get; init; }
    public bool IsPublicCommenter { get; init; }

    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

// ── Requests ──────────────────────────────────────────────────────────────────

public record CreateFileCommentRequest(string Text);

public record UpdateFileCommentRequest(string Text);

/// <summary>The four owner-controlled per-audience commenting toggles on an UploadFile.</summary>
public record FileCommentSettingsRecord(
    bool AllowInvestigationTeamComments,
    bool AllowClientComments,
    bool AllowOrganizationComments,
    bool AllowPublicComments);
