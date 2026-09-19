namespace Ben.Service.Models.Entities;

/// <summary>Metadata record — FileData is excluded to keep responses lightweight.</summary>
public record UploadFileRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileTypeId { get; init; }
    /// <summary>The owning person, or null once the file has been handed to a group (item 180 Phase B).</summary>
    public Guid? AppUserId { get; init; }
    /// <summary>The owning group, when a person handed the file over rather than destroy it.</summary>
    public Guid? OwnerOrganizationId { get; init; }
    public required string FileName { get; init; }
    public required string StoredFileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSize { get; init; }

    /// <summary>Relative path within file storage (e.g. "users/{userId}/{storedFileName}").</summary>
    public string? StoragePath { get; init; }

    public string? Description { get; init; }
    public bool IsPublic { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }

    /// <summary>Set when this file was created by clipping a parent audio file.</summary>
    public Guid?   ParentFileId { get; init; }
    public double? RegionStart  { get; init; }
    public double? RegionEnd    { get; init; }

    /// <summary>Fabric.js JSON snapshot for the image editor — present when the file has been edited.</summary>
    public string? EditStateJson    { get; init; }
    public bool    IsEditedVersion  { get; init; }

    // ── Comment settings (item #6 phase 2) ──────────────────────────────────
    public bool AllowInvestigationTeamComments { get; init; }
    public bool AllowClientComments { get; init; }
    public bool AllowOrganizationComments { get; init; }
    public bool AllowPublicComments { get; init; }

    /// <summary>Set when this file is an independent copy made for a case's Files tab (copy-on-attach).</summary>
    public Guid? CaseCopyOfUploadFileId { get; init; }

    /// <summary>Set when this row is an archived prior version kept by a replace (item #6 phase 3).</summary>
    public Guid? ArchivedFromUploadFileId { get; init; }

    // ── Who and what this file belongs to (V-3, site evaluation 2026-09-06) ──
    // Filled by the media-library listing only, and null everywhere else. The video editor's
    // Server tab showed seven identical "test-audio.mp3" rows — other people's uploads, reachable
    // through a shared group — with nothing to tell them apart. A file name is not an identity.
    //
    // Not mapped from the entity: both need a join, and every other consumer of this record would
    // pay for them unread. The listing sets them after the map.

    /// <summary>The person or group this file belongs to, for a listing that spans several.</summary>
    public string? OwnerDisplayName { get; init; }

    /// <summary>
    /// The case this file is attached to, as its reference (<c>#2026-003</c>), or null.
    /// </summary>
    /// <remarks>
    /// The first one, when a file reaches more than one case. A file belongs to a case three ways
    /// — a link on its Files tab, evidence on a timeline entry, and a published video project —
    /// and the point here is to tell two identically named rows apart, not to enumerate every
    /// place a file appears.
    /// </remarks>
    public string? CaseReference { get; init; }
}
