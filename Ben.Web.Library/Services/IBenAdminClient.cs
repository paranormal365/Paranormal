using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

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
    // ── Organizations ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns organizations visible to the current user, each with CanEdit and CanDelete flags.
    /// SuperAdmins see all organizations; others see only orgs they are active members of.
    /// </summary>
    Task<IReadOnlyList<OrganizationListItemResponse>> GetOrganizationsAsync(CancellationToken token = default);

    /// <summary>Returns a single organization for pre-filling the edit form. Returns null if not found or forbidden.</summary>
    Task<OrganizationAdminRecord?> GetOrganizationAsync(Guid id, CancellationToken token = default);

    /// <summary>Creates a new organization (SuperAdmin only).</summary>
    Task<OrganizationAdminRecord?> CreateOrganizationAsync(AdminCreateOrganizationRequest request, CancellationToken token = default);

    /// <summary>Updates an organization's Name and UrlName. Requires Update access or SuperAdmin.</summary>
    Task<OrganizationAdminRecord?> UpdateOrganizationAsync(Guid id, AdminUpdateOrganizationRequest request, CancellationToken token = default);

    /// <summary>Deletes an organization. Requires Delete access or SuperAdmin.</summary>
    Task<bool> DeleteOrganizationAsync(Guid id, CancellationToken token = default);

    // ── Roles ─────────────────────────────────────────────────────────────────

    /// <summary>Returns all site-level roles with the number of users currently assigned to each.</summary>
    Task<IReadOnlyList<AdminRoleWithCountResponse>> GetRolesAsync(CancellationToken token = default);

    /// <summary>Creates a new site-level role.</summary>
    Task<AppRoleAdminRecord?> CreateRoleAsync(string roleName, CancellationToken token = default);

    /// <summary>Deletes a role. Will fail if any users are still assigned to it.</summary>
    Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken token = default);

    // ── Audit Log ─────────────────────────────────────────────────────────────

    Task<AuditLogPagedResponse?> GetAuditLogsAsync(int page = 1, int pageSize = 50, string? entityType = null, int? action = null, Guid? userId = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken token = default);
    Task<IReadOnlyList<string>> GetAuditLogEntityTypesAsync(CancellationToken token = default);
    Task<bool> SendAuditLogMessageAsync(SendAuditLogMessageRequest request, CancellationToken token = default);

    // ── Users ─────────────────────────────────────────────────────────────────

    /// <summary>Returns a lightweight list of all application users.</summary>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    Task<IReadOnlyList<AppUserRecord>> GetAllUsersAsync(CancellationToken token = default);

    /// <summary>Returns the full detail aggregate for a single user, including addresses, emails, phones, links, notes, memberships, and files.</summary>
    /// <param name="userId">The <see cref="Guid"/> primary key of the user.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The detail record, or <c>null</c> if the user was not found.</returns>
    Task<AppUserDetailAdminRecord?> GetUserDetailAsync(Guid userId, CancellationToken token = default);

    /// <summary>Creates a new application user with an initial password.</summary>
    /// <param name="request">The new user fields including email, password, display name and role flags.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The created <see cref="AppUserAdminRecord"/>, or <c>null</c> if creation failed.</returns>
    Task<AppUserAdminRecord?> CreateUserAsync(AdminCreateUserRequest request, CancellationToken token = default);

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

    // ── Generic Lookup Types ──────────────────────────────────────────────────
    // Covers UserAddressType, UserEmailType, UserPhoneType, UserLinkType, UserNoteType,
    // UserMessageType, and the five Org equivalents — all share the same schema.

    /// <summary>Returns all rows for a lookup-type table at the given admin API route.</summary>
    Task<IReadOnlyList<LookupTypeAdminRecord>> GetLookupTypesAsync(string route, CancellationToken token = default);

    /// <summary>Creates a new row in a lookup-type table.</summary>
    Task<LookupTypeAdminRecord?> CreateLookupTypeAsync(string route, LookupTypeUpsertRequest request, CancellationToken token = default);

    /// <summary>Updates an existing row in a lookup-type table.</summary>
    Task<LookupTypeAdminRecord?> UpdateLookupTypeAsync(string route, Guid id, LookupTypeUpsertRequest request, CancellationToken token = default);

    /// <summary>Deletes a row from a lookup-type table.</summary>
    Task<bool> DeleteLookupTypeAsync(string route, Guid id, CancellationToken token = default);

    // ── CMS Pages ─────────────────────────────────────────────────────────────

    Task<IReadOnlyList<CmsPageListItem>> GetCmsPagesAsync(Guid orgId, CancellationToken token = default);
    Task<CmsPageDetail?> GetCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default);
    Task<CmsPageDetail?> CreateCmsPageAsync(Guid orgId, CmsCreatePageRequest request, CancellationToken token = default);
    Task<CmsPageDetail?> UpdateCmsPageAsync(Guid orgId, Guid pageId, CmsUpdatePageRequest request, CancellationToken token = default);
    Task<bool> DeleteCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default);

    // ── CMS Sections ──────────────────────────────────────────────────────────

    Task<CmsSectionRecord?> CreateCmsSectionAsync(Guid orgId, Guid pageId, CmsCreateSectionRequest request, CancellationToken token = default);
    Task<CmsSectionRecord?> UpdateCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CmsUpdateSectionRequest request, CancellationToken token = default);
    Task<bool> ReorderCmsSectionsAsync(Guid orgId, Guid pageId, IList<Guid> orderedIds, CancellationToken token = default);
    Task<bool> DeleteCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CancellationToken token = default);

    // ── Organization Logos ────────────────────────────────────────────────────

    Task<IReadOnlyList<OrganizationLogoRecord>> GetOrgLogosAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationLogoRecord?> CreateOrgLogoAsync(Guid orgId, CmsCreateLogoRequest request, CancellationToken token = default);
    Task<OrganizationLogoRecord?> UpdateOrgLogoAsync(Guid orgId, Guid logoId, CmsUpdateLogoRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgLogoAsync(Guid orgId, Guid logoId, CancellationToken token = default);

    // ── Organization Addresses ────────────────────────────────────────────────

    Task<IReadOnlyList<OrganizationAddressRecord>> GetOrgAddressesAsync(Guid orgId, CancellationToken token = default);
    Task<IReadOnlyList<OrganizationAddressTypeRecord>> GetOrgAddressTypesAsync(CancellationToken token = default);
    Task<OrganizationAddressRecord?> CreateOrgAddressAsync(Guid orgId, OrgAddressUpsertRequest request, CancellationToken token = default);
    Task<OrganizationAddressRecord?> UpdateOrgAddressAsync(Guid orgId, Guid addressId, OrgAddressUpsertRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgAddressAsync(Guid orgId, Guid addressId, CancellationToken token = default);
    Task<GeocodingPreviewResponse?> PreviewGeocodingAsync(string streetAddress1, string? streetAddress2, string city, string state, string zipCode, string country, CancellationToken token = default);
    Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken token = default);

    // ── Public Org Pages (no auth required) ──────────────────────────────────

    Task<OrgPublicHomeResponse?> GetPublicOrgAsync(string urlName, CancellationToken token = default);
    Task<OrgPublicPageResponse?> GetPublicOrgPageAsync(string urlName, string pageSlug, CancellationToken token = default);
    string GetFileDownloadUrl(Guid uploadFileId);

    // ── Organization Address Map Config ───────────────────────────────────────

    /// <summary>Returns the map display config for an organization address, or null if not configured.</summary>
    Task<AddressMapConfigRecord?> GetOrgAddressMapConfigAsync(Guid orgId, Guid addressId, CancellationToken token = default);

    /// <summary>Saves (upserts) the map display config for an organization address.</summary>
    Task<AddressMapConfigRecord?> UpsertOrgAddressMapConfigAsync(Guid orgId, Guid addressId, AddressMapConfigRecord config, CancellationToken token = default);

    /// <summary>Removes the map config for an organization address (resets to "not on map").</summary>
    Task<bool> DeleteOrgAddressMapConfigAsync(Guid orgId, Guid addressId, CancellationToken token = default);

    // ── Org Member Groups ─────────────────────────────────────────────────────

    Task<IReadOnlyList<OrgMembershipItem>> GetOrganizationMembersAsync(Guid orgId, CancellationToken token = default);
    Task<IReadOnlyList<OrgMemberGroupRecord>> GetGroupsAsync(Guid orgId, CancellationToken token = default);

    // ── Membership Requests ───────────────────────────────────────────────────

    /// <summary>Returns all membership requests for the organization (requires MembershipRequests-Read permission).</summary>
    Task<IReadOnlyList<OrganizationMembershipRequestRecord>> GetMembershipRequestsAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Returns the current user's membership request for the organization, or null if none exists.</summary>
    Task<OrganizationMembershipRequestRecord?> GetMyMembershipRequestAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Submits a membership application to the organization.</summary>
    Task<OrganizationMembershipRequestRecord?> ApplyForMembershipAsync(Guid orgId, string? message, CancellationToken token = default);

    /// <summary>Accepts or denies a pending membership application (requires MembershipRequests-Update permission).</summary>
    Task<OrganizationMembershipRequestRecord?> RespondToMembershipRequestAsync(Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote, CancellationToken token = default);

    /// <summary>Withdraws the applicant's own pending request.</summary>
    Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default);

    // ── Organization Files ────────────────────────────────────────────────────

    /// <summary>Returns all files owned by the organization.</summary>
    Task<IReadOnlyList<OrganizationFileRecord>> GetOrgFilesAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Returns the deletion audit log for organization files.</summary>
    Task<IReadOnlyList<OrganizationFileDeleteLogRecord>> GetOrgFileDeleteLogAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Uploads a new file to the organization's file library.</summary>
    Task<OrganizationFileRecord?> UploadOrgFileAsync(Guid orgId, MultipartFormDataContent content, CancellationToken token = default);

    /// <summary>
    /// Copies a user's public or org-shared file into the organization's file library.
    /// Returns the created file plus flags indicating whether the caller can/did publish immediately.
    /// </summary>
    Task<OrgFileCopyClientResult?> CopyFileFromUserAsync(Guid orgId, Guid uploadFileId, string? description, bool publishImmediately, CancellationToken token = default);

    /// <summary>Approves or revokes public access for an organization file. Logs approver and timestamp.</summary>
    Task<OrganizationFileRecord?> PublishOrgFileAsync(Guid orgId, Guid fileId, bool isPublic, CancellationToken token = default);

    /// <summary>Updates metadata (description, sort order) of an organization file.</summary>
    Task<OrganizationFileRecord?> UpdateOrgFileAsync(Guid orgId, Guid fileId, string? description, int sortOrder, CancellationToken token = default);

    /// <summary>Permanently deletes an organization-owned file. Writes an audit log before deleting.</summary>
    Task<bool> DeleteOrgFileAsync(Guid orgId, Guid fileId, CancellationToken token = default);
    Task<OrgMemberGroupRecord?> CreateGroupAsync(Guid orgId, OrgGroupUpsertRequest request, CancellationToken token = default);
    Task<OrgMemberGroupRecord?> UpdateGroupAsync(Guid orgId, Guid groupId, OrgGroupUpsertRequest request, CancellationToken token = default);
    Task<bool> DeleteGroupAsync(Guid orgId, Guid groupId, CancellationToken token = default);
    Task<IReadOnlyList<OrgMemberGroupMembershipRecord>> GetGroupMembersAsync(Guid orgId, Guid groupId, CancellationToken token = default);
    Task<OrgMemberGroupMembershipRecord?> AddGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default);
    Task<bool> RemoveGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default);

    // ── Organization Roles ────────────────────────────────────────────────────────
    Task<IReadOnlyList<OrganizationRoleRecord>> GetOrgRolesAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationRoleRecord?> GetOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<OrganizationRoleRecord?> CreateOrgRoleAsync(Guid orgId, CreateOrgRoleRequest request, CancellationToken token = default);
    Task<OrganizationRoleRecord?> UpdateOrgRoleAsync(Guid orgId, Guid roleId, UpdateOrgRoleRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<IReadOnlyList<OrganizationRolePermissionRecord>> GetOrgRolePermissionsAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<bool> SetOrgRolePermissionsAsync(Guid orgId, Guid roleId, IEnumerable<SetRolePermissionRequest> permissions, CancellationToken token = default);
    Task<IReadOnlyList<OrganizationRoleMembershipRecord>> GetOrgRoleMembersAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<OrganizationRoleMembershipRecord?> AddOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid orgUserMembershipId, CancellationToken token = default);
    Task<bool> RemoveOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid membershipId, CancellationToken token = default);

    // ── CMS Page Permissions ──────────────────────────────────────────────────

    Task<IReadOnlyList<CmsPagePermissionRecord>> GetPagePermissionsAsync(Guid orgId, Guid pageId, CancellationToken token = default);
    Task<CmsPagePermissionRecord?> CreatePagePermissionAsync(Guid orgId, Guid pageId, PagePermissionCreateRequest request, CancellationToken token = default);
    Task<CmsPagePermissionRecord?> UpdatePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CmsPageAction actions, CancellationToken token = default);
    Task<bool> DeletePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CancellationToken token = default);

    // ── User sub-entity type lists (for dropdowns) ────────────────────────────

    Task<IReadOnlyList<UserAddressTypeRecord>> GetUserAddressTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserEmailTypeRecord>> GetUserEmailTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserPhoneTypeRecord>> GetUserPhoneTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserLinkTypeRecord>> GetUserLinkTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserNoteTypeRecord>> GetUserNoteTypesAsync(CancellationToken token = default);

    // Type management (SuperAdmin creates new types)
    Task<bool> CreateUserAddressTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserEmailTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserPhoneTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserLinkTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserNoteTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);

    // ── User sub-entity CRUD (SuperAdmin) ─────────────────────────────────────

    Task<bool> CreateUserAddressAsync(Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserAddressAsync(Guid id, Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserAddressAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserEmailAsync(Guid userId, Guid actorId, UserEmailUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserEmailAsync(Guid id, Guid userId, Guid actorId, UserEmailUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserEmailAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserPhoneAsync(Guid userId, Guid actorId, UserPhoneUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserPhoneAsync(Guid id, Guid userId, Guid actorId, UserPhoneUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserPhoneAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserLinkAsync(Guid userId, Guid actorId, UserLinkUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserLinkAsync(Guid id, Guid userId, Guid actorId, UserLinkUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserLinkAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserNoteAsync(Guid userId, Guid actorId, UserNoteUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserNoteAsync(Guid id, Guid userId, Guid actorId, UserNoteUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserNoteAsync(Guid id, CancellationToken token = default);

    Task<bool> DeleteUploadFileAdminAsync(Guid id, CancellationToken token = default);

    // ── CMS File Library ──────────────────────────────────────────────────────

    /// <summary>Returns upload files shared with the given organization (for logo/gallery selection).</summary>
    Task<IReadOnlyList<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Downloads raw file bytes + content-type for in-browser thumbnail rendering.</summary>
    Task<(byte[] Data, string ContentType)?> GetFileDataAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Returns all active upload file types (used to choose a type when uploading a logo).</summary>
    Task<IReadOnlyList<UploadFileTypeRecord>> GetPublicFileTypesAsync(CancellationToken token = default);

    /// <summary>Uploads an image file and returns its record. Used to add a logo from device.</summary>
    Task<UploadFileRecord?> UploadImageAsync(Guid fileTypeId, Guid userId, string fileName, string contentType, byte[] data, CancellationToken token = default);

    /// <summary>
    /// Uploads any file (audio, document, image, etc.) for a specific user.
    /// Use when the caller controls the description and public-visibility flag.
    /// </summary>
    Task<UploadFileRecord?> UploadUserFileAsync(
        Guid fileTypeId, Guid userId,
        string fileName, string contentType, byte[] data,
        string? description = null, bool isPublic = false,
        CancellationToken token = default);

    // ── Audio Config ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the saved WaveSurfer config for an audio UploadFile,
    /// or <c>null</c> if none has been saved (component uses defaults).
    /// </summary>
    Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Creates or fully replaces the WaveSurfer config for an audio UploadFile.</summary>
    Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default);

    /// <summary>Removes the saved WaveSurfer config; the player will use theme-derived defaults on next render.</summary>
    Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default);

    // ── Region Notes ──────────────────────────────────────────────

    /// <summary>Returns all region notes for the given file, ordered by region start then time offset.</summary>
    Task<IReadOnlyList<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Creates a new region note and returns the persisted record.</summary>
    Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default);

    /// <summary>Updates an existing region note (text, public flag, time offset).</summary>
    Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default);

    /// <summary>Permanently deletes a region note.</summary>
    Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default);

    // ── Audio Clip ─────────────────────────────────────────────────

    /// <summary>
    /// Clips the audio of <paramref name="fileId"/> to the specified time range and saves the
    /// result as a new UploadFile. Currently supports WAV and MP3 sources; output is WAV.
    /// </summary>
    Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default);

    /// <summary>
    /// Returns clipped audio bytes for the given time range WITHOUT saving a new file.
    /// Used by <c>WsRegionExplorer</c> to load only the region's audio.
    /// Returns null if the source format is unsupported (non-WAV / non-MP3).
    /// </summary>
    Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default);

    /// <summary>Returns all child clip files that were derived from <paramref name="fileId"/> via the region-clip workflow.</summary>
    Task<IReadOnlyList<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default);

    // ── Votes ──────────────────────────────────────────────────────

    /// <summary>Returns the aggregated vote summary including the current user's vote (if any).</summary>
    Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default);

    /// <summary>
    /// Creates or updates the current user's vote (upsert).
    /// Pass score 1 for upvote, -1 for downvote.
    /// </summary>
    Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default);

    /// <summary>Removes the current user's vote. No-op if the user has not voted.</summary>
    Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default);

    // ── Directions ────────────────────────────────────────────────────────────
    Task<DirectionsResult?> GetDirectionsAsync(double fromLat, double fromLon, double toLat, double toLon, CancellationToken token = default);

    // ── Org address member access ──────────────────────────────────────────────
    Task<IReadOnlyList<OrganizationAddressMemberAccessRecord>> GetAddressMemberAccessAsync(Guid orgId, Guid addressId, CancellationToken token = default);
    Task<OrganizationAddressMemberAccessRecord?> AddAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid orgUserMembershipId, CancellationToken token = default);
    Task<bool> RemoveAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid accessId, CancellationToken token = default);

    // ── Org settings ──────────────────────────────────────────────────────────
    Task<OrgSettingsResponse?> GetOrgSettingsAsync(Guid orgId, CancellationToken token = default);
    Task<OrgSettingsResponse?> UpdateOrgSettingsAsync(Guid orgId, OrgSettingsRequest request, CancellationToken token = default);
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

/// <summary>Organization list row returned by <c>GET /api/organizations</c>.</summary>
public sealed record OrganizationListItemResponse(
    Guid Id,
    string Name,
    string UrlName,
    DateTime DateCreated,
    bool CanEdit,
    bool CanDelete);

/// <summary>Request body for creating a new organization (SuperAdmin only).</summary>
public sealed record AdminCreateOrganizationRequest(string Name, string UrlName);

/// <summary>Request body for updating an organization's Name and UrlName.</summary>
public sealed record AdminUpdateOrganizationRequest(string Name, string UrlName);

/// <summary>Role record paired with its current user count.</summary>
public sealed record AdminRoleWithCountResponse(AppRoleAdminRecord Role, int UserCount);

// ── Public org page response records ─────────────────────────────────────────

public sealed record OrgAddressUpsertRequest(
    Guid   OrganizationAddressTypeId,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string Country,
    int    SortOrder,
    OrganizationAddressVisibility  Visibility          = OrganizationAddressVisibility.Private,
    OrganizationAddressDisplayMode PublicDisplayMode   = OrganizationAddressDisplayMode.Hidden,
    OrganizationAddressDisplayMode MemberDisplayMode   = OrganizationAddressDisplayMode.FullAddressAndMap,
    bool   IsSearchable     = false,
    OrganizationAddressVisibility  SearchVisibility    = OrganizationAddressVisibility.Public,
    double? SearchRadiusMiles = null,
    decimal? Latitude  = null,
    decimal? Longitude = null);

public sealed record GeocodingPreviewResponse(decimal? Latitude, decimal? Longitude, string? ResultType);

public sealed record ReverseGeocodingResponse(
    string? StreetAddress1,
    string? City,
    string? State,
    string? ZipCode,
    string? Country);

public sealed record OrgPublicHomeResponse(
    Guid OrgId, string OrgName, string OrgUrlName,
    IReadOnlyList<OrgPublicLogoItem> Logos,
    OrgPublicPageItem? HomePage,
    IReadOnlyList<OrgPublicNavItem> NavPages);

public sealed record OrgPublicPageResponse(
    Guid OrgId, string OrgName, string OrgUrlName,
    IReadOnlyList<OrgPublicLogoItem> Logos,
    OrgPublicPageItem Page,
    IReadOnlyList<OrgPublicNavItem> NavPages);

public sealed record OrgPublicLogoItem(Guid LogoId, Guid UploadFileId, string? AltText, int SortOrder);
public sealed record OrgPublicPageItem(Guid Id, string PageTitle, string UrlName, bool IsHome, IReadOnlyList<OrgPublicSectionItem> Sections);
public sealed record OrgPublicSectionItem(Guid Id, CmsSectionType SectionType, string? Title, string ContentJson, int SortOrder);
public sealed record OrgPublicNavItem(Guid Id, string PageTitle, string UrlName, Guid? ParentPageId, int SortOrder);

// AuditLogRecord, AuditLogPagedResponse, SendAuditLogMessageRequest
// are defined in Ben.Service.Models.Admin (via project reference).

/// <summary>
/// Request body for creating a new application user.
/// Mirrors the <c>AdminCreateUserRequest</c> DTO in <c>Ben.Data.WebApi</c>.
/// </summary>
public sealed record AdminCreateUserRequest(
    string Email,
    string Password,
    string? DisplayName,
    string? UserName,
    bool IsEmailConfirmed,
    bool IsSuperAdmin);

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

// ── CMS types ─────────────────────────────────────────────────────────────────

/// <summary>Page row returned by GET /api/organizations/{orgId}/pages.</summary>
public sealed record CmsPageListItem(
    Guid Id,
    Guid OrganizationId,
    Guid? ParentPageId,
    string PageTitle,
    string UrlName,
    bool IsHome,
    bool IsPublished,
    bool IsPublic,
    int SortOrder,
    int SectionCount,
    bool CanEdit,
    bool CanDelete,
    DateTime DateCreated);

/// <summary>Full page with sections returned by GET /api/organizations/{orgId}/pages/{pageId}.</summary>
public sealed record CmsPageDetail(
    Guid Id,
    Guid OrganizationId,
    Guid? ParentPageId,
    string PageTitle,
    string UrlName,
    string PageHtml,
    bool IsHome,
    bool IsPublished,
    bool IsPublic,
    int SortOrder,
    DateTime DateCreated,
    DateTime? DateUpdated,
    IReadOnlyList<CmsSectionRecord> Sections);

public sealed record CmsCreatePageRequest(string PageTitle, string UrlName, string? PageHtml, bool IsPublic, Guid? ParentPageId, int SortOrder);
public sealed record CmsUpdatePageRequest(string PageTitle, string UrlName, string? PageHtml, bool IsPublished, bool IsPublic, Guid? ParentPageId, int SortOrder);
public sealed record CmsCreateSectionRequest(CmsSectionType SectionType, string? Title, string ContentJson, int SortOrder, bool IsActive);
public sealed record CmsUpdateSectionRequest(string? Title, string ContentJson, bool IsActive);
public sealed record CmsCreateLogoRequest(Guid UploadFileId, string? AltText, bool IsActive, int SortOrder);
public sealed record CmsUpdateLogoRequest(string? AltText, bool IsActive, int SortOrder);

// ── User sub-entity request records ──────────────────────────────────────────

public sealed record UserAddressUpsertRequest(
    Guid UserAddressTypeId,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string Country,
    bool IsPublic,
    int SortOrder = 0,
    decimal? Latitude  = null,
    decimal? Longitude = null);

public sealed record UserEmailUpsertRequest(
    Guid UserEmailTypeId,
    string EmailAddress,
    bool IsPrimary,
    bool IsPublic,
    int SortOrder = 0);

public sealed record UserPhoneUpsertRequest(
    Guid UserPhoneTypeId,
    string PhoneNumber,
    string? PhoneCountry,
    bool IsPrimary,
    bool IsCellular,
    bool IsPublic);

public sealed record UserLinkUpsertRequest(
    Guid UserLinkTypeId,
    string LinkUrl,
    string? DisplayText,
    bool IsPublic,
    bool IsActive);

public sealed record UserNoteUpsertRequest(
    Guid UserNoteTypeId,
    string NoteSubject,
    string NoteBody,
    bool IsPublic);

/// <summary>
/// Request body for creating or fully replacing a lookup-type row
/// (UserAddressType, OrganizationEmailType, etc.).
/// The Id field must be set to the existing Id on update, or <see cref="Guid.Empty"/> on create.
/// </summary>
public sealed record LookupTypeUpsertRequest(
    Guid Id,
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    bool IsActive,
    bool IsPublic,
    int SortOrder,
    DateTime DateCreated,
    DateTime? DateUpdated,
    Guid CreatedByAppUserId,
    Guid? UpdatedByAppUserId);

public sealed record OrgGroupUpsertRequest(string Name, string? Description, bool IsActive, int SortOrder);

public sealed record CreateOrgRoleRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record UpdateOrgRoleRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record SetRolePermissionRequest(OrganizationSecurityTable TableName, OrganizationSecurityAction Actions);

public sealed record PagePermissionCreateRequest(Guid? AppUserId, Guid? OrgMemberGroupId, CmsPageAction Actions);

/// <summary>Org membership row from GET /api/organizations/{orgId}/security/users.</summary>
public sealed record OrgMembershipItem(Guid MembershipId, Guid AppUserId, OrganizationMemberRole Role, bool IsActive);

public sealed record DirectionsResult(
    string RouteGeoJson,
    IReadOnlyList<RoutePoint> RoutePoints,
    double TotalDistanceMiles,
    double TotalDurationMinutes,
    IReadOnlyList<RouteStep> Steps);

public sealed record RoutePoint(double Lat, double Lon);

public sealed record RouteStep(string Instruction, double DistanceMiles, double DurationSeconds);

public sealed record OrgSettingsResponse(bool ShowAddressMap, bool ShowAddressDirections);
public sealed record OrgSettingsRequest(bool ShowAddressMap, bool ShowAddressDirections);
public sealed record AddAddressMemberAccessRequest(Guid OrganizationUserMembershipId);
