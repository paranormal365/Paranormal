using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;

namespace Ben.Web.Library.Services;

/// <summary>
/// Defines the SuperAdmin operations available to Blazor library components.
/// </summary>
/// <remarks>
/// Implemented by <c>BenAdminClientAdapter</c> in <c>Ben.Web.WebApp</c>, which
/// delegates every call to the typed <c>IWebApiClient</c> HTTP client.
/// Library components depend on this interface so that <c>Ben.Web.Library</c>
/// does not need a direct reference to the WebApp project.
/// <para>
/// All methods require an active SuperAdmin bearer token; calls made by a
/// non-SuperAdmin session will be rejected by the WebApi with HTTP 403.
/// </para>
/// </remarks>
public interface IBenAdminClient
{
    // ── Users ─────────────────────────────────────────────────────────────────

    /// <summary>Returns a lightweight list of all application users.</summary>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    Task<IReadOnlyList<AppUserRecord>> GetAllUsersAsync(CancellationToken token = default);

    /// <summary>Returns the full detail aggregate for a single user, including addresses, emails, phones, links, notes, memberships, and files.</summary>
    /// <param name="userId">The <see cref="Guid"/> primary key of the user.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The detail record, or <c>null</c> if the user was not found.</returns>
    Task<AppUserDetailAdminRecord?> GetUserDetailAsync(Guid userId, CancellationToken token = default);

    /// <summary>Updates editable profile fields for a user including audit timestamps.</summary>
    /// <param name="userId">The <see cref="Guid"/> primary key of the user to update.</param>
    /// <param name="request">The updated profile values.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The updated admin record, or <c>null</c> if the update failed.</returns>
    Task<AppUserAdminRecord?> UpdateUserProfileAsync(Guid userId, AdminUpdateUserProfileRequest request, CancellationToken token = default);

    // ── Impersonation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Begins an impersonation session as <paramref name="targetUserId"/>.
    /// Saves the current SuperAdmin token and replaces it with a token issued for the target user.
    /// </summary>
    /// <param name="targetUserId">The user to impersonate.</param>
    /// <param name="targetUserEmail">Display email stored alongside the impersonation token.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns><c>true</c> if impersonation succeeded; <c>false</c> otherwise.</returns>
    Task<bool> ImpersonateUserAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default);

    /// <summary>
    /// Ends the active impersonation session and restores the original SuperAdmin token.
    /// </summary>
    /// <remarks>This is a synchronous in-memory operation; no HTTP call is made.</remarks>
    void StopImpersonating();

    // ── File Types ────────────────────────────────────────────────────────────

    /// <summary>Returns all upload file types together with their allowed extension patterns.</summary>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    Task<IReadOnlyList<AdminFileTypeWithExtensionsResponse>> GetFileTypesWithExtensionsAsync(CancellationToken token = default);

    /// <summary>Creates a new upload file type.</summary>
    /// <param name="request">Fields for the new file type including display metadata and the <c>AllowAllExtensions</c> flag.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The created <see cref="UploadFileTypeRecord"/>, or <c>null</c> if creation failed.</returns>
    Task<UploadFileTypeRecord?> CreateFileTypeAsync(AdminCreateFileTypeRequest request, CancellationToken token = default);

    /// <summary>Updates an existing upload file type.</summary>
    /// <param name="id">The primary key of the file type to update.</param>
    /// <param name="request">Replacement field values.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The updated record, or <c>null</c> if the update failed.</returns>
    Task<UploadFileTypeRecord?> UpdateFileTypeAsync(Guid id, AdminUpdateFileTypeRequest request, CancellationToken token = default);

    /// <summary>Deletes an upload file type and cascades to all of its extension patterns.</summary>
    /// <param name="id">The primary key of the file type to delete.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns><c>true</c> if deletion succeeded; <c>false</c> otherwise.</returns>
    Task<bool> DeleteFileTypeAsync(Guid id, CancellationToken token = default);

    // ── File Type Extensions ──────────────────────────────────────────────────

    /// <summary>Adds an extension pattern to an existing upload file type.</summary>
    /// <param name="request">The file type ID, pattern string (e.g. <c>.txt</c> or <c>.tx*</c>), and creator ID.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The created <see cref="UploadFileTypeExtensionRecord"/>, or <c>null</c> if creation failed.</returns>
    Task<UploadFileTypeExtensionRecord?> CreateFileTypeExtensionAsync(AdminCreateFileTypeExtensionRequest request, CancellationToken token = default);

    /// <summary>Replaces the pattern string of an existing extension record.</summary>
    /// <param name="id">The primary key of the extension to update.</param>
    /// <param name="pattern">The new pattern string (e.g. <c>.pdf</c> or <c>.doc*</c>).</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The updated <see cref="UploadFileTypeExtensionRecord"/>, or <c>null</c> if the update failed.</returns>
    Task<UploadFileTypeExtensionRecord?> UpdateFileTypeExtensionAsync(Guid id, string pattern, CancellationToken token = default);

    /// <summary>Removes a single extension pattern from its parent file type.</summary>
    /// <param name="id">The primary key of the extension to delete.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns><c>true</c> if deletion succeeded; <c>false</c> otherwise.</returns>
    Task<bool> DeleteFileTypeExtensionAsync(Guid id, CancellationToken token = default);
}

/// <summary>
/// Combined result returned by <c>GET /api/admin/upload-file-types/with-extensions</c>.
/// </summary>
/// <param name="FileType">The file type record.</param>
/// <param name="Extensions">All extension patterns currently registered for the file type.</param>
public sealed record AdminFileTypeWithExtensionsResponse(
    UploadFileTypeRecord FileType,
    IReadOnlyList<UploadFileTypeExtensionRecord> Extensions);

/// <summary>Request body for creating a new <c>UploadFileType</c>.</summary>
public sealed record AdminCreateFileTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    bool AllowAllExtensions,
    Guid CreatedByAppUserId);

/// <summary>Request body for updating an existing <c>UploadFileType</c>.</summary>
public sealed record AdminUpdateFileTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    bool AllowAllExtensions,
    Guid? UpdatedByAppUserId);

/// <summary>Request body for adding an extension pattern to an existing <c>UploadFileType</c>.</summary>
public sealed record AdminCreateFileTypeExtensionRequest(
    Guid UploadFileTypeId,
    /// <summary>Pattern string, e.g. <c>.txt</c> (exact) or <c>.doc*</c> (wildcard suffix). See <c>FileExtensionPatternMatcher</c>.</summary>
    string Pattern,
    Guid CreatedByAppUserId);

/// <summary>
/// Request body for updating a user's editable profile fields.
/// Mirrors the <c>AdminUpdateUserProfileRequest</c> DTO in <c>Ben.Data.WebApi</c>.
/// </summary>
public sealed record AdminUpdateUserProfileRequest(
    string? DisplayName,
    string? UserName,
    string? Email,
    string? PhoneNumber,
    bool IsEmailConfirmed,
    bool IsTwoFactorEnabled,
    bool IsLockoutEnabled,
    DateTimeOffset? LockoutEnd,
    DateTime DateCreated,
    DateTime? DateUpdated);

