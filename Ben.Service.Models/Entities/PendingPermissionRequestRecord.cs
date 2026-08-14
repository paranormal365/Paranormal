using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// A pending file-permission request, carrying the names a reviewer needs to decide it.
/// </summary>
/// <remarks>
/// <c>UploadFilePermissionRequestRecord</c> is all raw ids, which is fine for the per-file view
/// where the file is already known and the page can resolve the rest. A reviewer working through a
/// list has no such context — "someone wants Use on some file" is not a decidable request — so this
/// projection joins the file and requester names in the same query rather than making the UI issue
/// a lookup per row.
/// </remarks>
/// <param name="Id">The request id, for approve/deny.</param>
/// <param name="UploadFileId">The file being requested.</param>
/// <param name="FileName">Original file name, for display.</param>
/// <param name="OrganizationId">The org the request is scoped to, or null for person-to-person.</param>
/// <param name="OrganizationName">That org's name, when scoped to one.</param>
/// <param name="RequestedByAppUserId">Who asked.</param>
/// <param name="RequestedByDisplayName">Their display name, falling back to their email.</param>
/// <param name="PermissionType">What they asked for.</param>
/// <param name="RequestNotes">Why, in their words.</param>
/// <param name="DateCreated">When they asked.</param>
public sealed record PendingPermissionRequestRecord(
    Guid Id,
    Guid UploadFileId,
    string? FileName,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid RequestedByAppUserId,
    string? RequestedByDisplayName,
    FilePermissionType PermissionType,
    string? RequestNotes,
    DateTime DateCreated);
