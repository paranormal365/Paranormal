using Ben.Data.Common.Enums;

namespace Ben.Web.WebApp.Services.WebApi;

// ── Upload File ──────────────────────────────────────────────────────────────
public sealed record UpdateUploadFileRequest(
    Guid UploadFileTypeId,
    string? Description,
    bool IsPublic,
    int SortOrder,
    Guid? UpdatedByAppUserId);

// ── Org Sharing ──────────────────────────────────────────────────────────────
// SharedByAppUserId/UpdatedByAppUserId dropped — the server now derives the acting user from the
// bearer token (GetCurrentUserIdOrThrow), never from client input; see UploadFileShareController.
public sealed record ShareFileWithOrgRequest(
    Guid OrganizationId,
    FileShareVisibility Visibility);

public sealed record UpdateOrgShareVisibilityRequest(FileShareVisibility Visibility);

public sealed class UploadFileOrgShareResponse
{
    public Guid Id { get; set; }
    public Guid UploadFileId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid SharedByAppUserId { get; set; }
    public FileShareVisibility Visibility { get; set; }
    public bool IsActive { get; set; }
    public DateTime DateCreated { get; set; }
}

// ── Permission Requests ──────────────────────────────────────────────────────
public sealed record SubmitPermissionRequestRequest(
    Guid? OrganizationId,
    Guid RequestedByAppUserId,
    FilePermissionType PermissionType,
    string? RequestNotes);

public sealed record ReviewPermissionRequestRequest(
    FilePermissionRequestStatus RequestStatus,
    string? ReviewNotes,
    Guid ReviewedByAppUserId);

public sealed class UploadFilePermissionRequestResponse
{
    public Guid Id { get; set; }
    public Guid UploadFileId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid RequestedByAppUserId { get; set; }
    public FilePermissionType PermissionType { get; set; }
    public FilePermissionRequestStatus RequestStatus { get; set; }
    public string? RequestNotes { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid? ReviewedByAppUserId { get; set; }
    public DateTime? DateReviewed { get; set; }
    public DateTime DateCreated { get; set; }
}
