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

    // ── Organization Area of Operation ────────────────────────────────────────

    Task<OrganizationAreaOfOperationRecord?> GetOrgAreaOfOperationAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationAreaOfOperationRecord?> UpsertOrgAreaOfOperationAsync(Guid orgId, UpsertAreaOfOperationRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgAreaOfOperationAsync(Guid orgId, CancellationToken token = default);
    Task<bool> UpdateClientAcceptanceAsync(Guid orgId, bool isAcceptingClients, bool acceptsClientsOutsideRange, CancellationToken token = default);

    /// <summary>
    /// Public search — no auth required. Returns orgs ordered by proximity.
    /// Center coordinates are NOT included in results.
    /// </summary>
    Task<IReadOnlyList<OrgSearchResult>> SearchOrganizationsAsync(double lat, double lon, int maxResults = 20, CancellationToken token = default);

    // ── Organization Addresses ────────────────────────────────────────────────

    Task<IReadOnlyList<OrganizationAddressRecord>> GetOrgAddressesAsync(Guid orgId, CancellationToken token = default);
    Task<IReadOnlyList<OrganizationAddressTypeRecord>> GetOrgAddressTypesAsync(CancellationToken token = default);
    Task<OrganizationAddressRecord?> CreateOrgAddressAsync(Guid orgId, OrgAddressUpsertRequest request, CancellationToken token = default);
    Task<OrganizationAddressRecord?> UpdateOrgAddressAsync(Guid orgId, Guid addressId, OrgAddressUpsertRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgAddressAsync(Guid orgId, Guid addressId, CancellationToken token = default);
    Task<GeocodingPreviewResponse?> PreviewGeocodingAsync(string streetAddress1, string? streetAddress2, string city, string state, string zipCode, string country, CancellationToken token = default);
    Task<GeocodingPreviewResponse?> SearchGeocodingAsync(string query, CancellationToken token = default);
    Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken token = default);

    // ── Public Org Pages (no auth required) ──────────────────────────────────

    Task<OrgPublicHomeResponse?> GetPublicOrgAsync(string urlName, CancellationToken token = default);
    Task<OrgPublicPageResponse?> GetPublicOrgPageAsync(string urlName, string pageSlug, CancellationToken token = default);
    string GetFileDownloadUrl(Guid uploadFileId);
    string GetOrgFileDownloadUrl(Guid orgId, Guid orgFileId);

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
    Task<OrganizationMembershipRequestRecord?> RespondToMembershipRequestAsync(Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote, bool? canReapply = null, string? denialReason = null, CancellationToken token = default);

    /// <summary>Withdraws the applicant's own pending request.</summary>
    Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default);

    // ── Membership Questions (Phase 3) ────────────────────────────────────────
    Task<IReadOnlyList<OrganizationMembershipQuestionRecord>> GetMembershipQuestionsAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationMembershipQuestionRecord?> CreateMembershipQuestionAsync(Guid orgId, UpsertMembershipQuestionRequest request, CancellationToken token = default);
    Task<OrganizationMembershipQuestionRecord?> UpdateMembershipQuestionAsync(Guid orgId, Guid id, UpsertMembershipQuestionRequest request, CancellationToken token = default);
    Task<bool> DeleteMembershipQuestionAsync(Guid orgId, Guid id, CancellationToken token = default);

    // ── Membership Voting (Phase 3) ───────────────────────────────────────────
    Task<OrganizationMembershipRequestRecord?> OpenMembershipVoteAsync(Guid orgId, Guid requestId, DateTime voteDeadline, CancellationToken token = default);
    Task<MembershipReviewVoteRecord?> CastMembershipVoteAsync(Guid orgId, Guid requestId, Ben.Data.Common.Enums.MembershipVoteType voteType, string? comment, CancellationToken token = default);
    Task<IReadOnlyList<MembershipReviewVoteRecord>> GetMembershipVotesAsync(Guid orgId, Guid requestId, CancellationToken token = default);

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

    // ── Case Transfers ────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseTransferLogRecord>> GetCaseTransfersAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseTransferLogRecord?> ProposeCaseTransferAsync(Guid orgId, Guid caseId, Guid toOrganizationId, string? reason, CancellationToken token = default);
    Task<CaseTransferLogRecord?> RespondCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, bool accept, string? rejectionReason, CancellationToken token = default);
    /// <summary>Cancels an outgoing pending transfer proposed by this org. Only the proposing org can cancel.</summary>
    Task<CaseTransferLogRecord?> CancelCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, CancellationToken token = default);

    // ── Public Case Discovery ─────────────────────────────────────────────────

    Task<IReadOnlyList<PublicCaseListItem>> GetPublicCasesAsync(string orgUrlName, CancellationToken token = default);
    /// <summary>Returns a single public case by org URL name and case reference (e.g. "2026-042").</summary>
    Task<PublicCaseDetail?> GetPublicCaseAsync(string orgUrlName, string caseRef, CancellationToken token = default);

    /// <summary>
    /// Returns a paginated, cross-organization list of all public cases worldwide,
    /// with city-level approximate coordinates and aggregated vote counts.
    /// Used to drive the home-page investigation map and ranked list.
    /// </summary>
    /// <param name="sort">"votes" (default) sorts by total votes desc; "date" sorts by open date desc.</param>
    Task<PublicCaseDiscoveryPagedResponse?> GetPublicCaseDiscoveryAsync(int page = 1, int pageSize = 20, string sort = "votes", CancellationToken token = default);

    // ── Case votes (community rating) ─────────────────────────────────────────

    /// <summary>
    /// Returns the aggregate vote summary for a public case.
    /// Anonymous-friendly: <c>CurrentUserVote</c> is non-null only when the bearer token is present.
    /// Calls <c>GET api/public/cases/{caseId}/votes</c>.
    /// </summary>
    Task<CaseVoteSummary?> GetCaseVoteSummaryAsync(Guid caseId, CancellationToken token = default);

    /// <summary>
    /// Returns vote summaries for multiple cases in one request.
    /// Used by <c>PublicCaseDiscovery.razor</c> to pre-load summaries for all visible
    /// list-cards without N individual requests. Calls <c>GET api/public/cases/vote-summaries</c>.
    /// </summary>
    Task<IReadOnlyList<CaseVoteSummary>> GetCaseVoteSummariesAsync(IEnumerable<Guid> caseIds, CancellationToken token = default);

    Task<CaseVoteSummary?> CastCaseVoteAsync(Guid caseId, Ben.Data.Common.Enums.EvidenceVoteType voteType, CancellationToken token = default);
    Task<bool> RemoveCaseVoteAsync(Guid caseId, CancellationToken token = default);

    // ── Investigations ────────────────────────────────────────────────────────

    Task<IReadOnlyList<InvestigationRecord>> GetInvestigationsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<InvestigationRecord?> GetInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<InvestigationRecord?> CreateInvestigationAsync(Guid orgId, Guid caseId, UpsertInvestigationRequest request, CancellationToken token = default);
    Task<InvestigationRecord?> UpdateInvestigationAsync(Guid orgId, Guid caseId, Guid id, UpsertInvestigationRequest request, CancellationToken token = default);
    Task<bool> DeleteInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<bool> CancelInvestigationByOrgAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<IReadOnlyList<InvestigationAttendeeRecord>> GetInvestigationAttendeesAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default);
    Task<InvestigationAttendeeRecord?> AddInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, AddInvestigationAttendeeRequest request, CancellationToken token = default);
    Task<InvestigationAttendeeRecord?> UpdateInvestigationAttendanceAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, bool? didAttend, string? assignedRole, Ben.Data.Common.Enums.RsvpStatus? rsvp = null, CancellationToken token = default);
    Task<bool> RemoveInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, CancellationToken token = default);

    // ── Evidence Voting ───────────────────────────────────────────────────────

    Task<EvidenceVoteSummary?> GetEvidenceVoteSummaryAsync(Guid uploadFileId, CancellationToken token = default);
    Task<IReadOnlyList<EvidenceVoteRecord>> GetEvidenceVotesAsync(Guid uploadFileId, CancellationToken token = default);
    Task<EvidenceVoteSummary?> CastEvidenceVoteAsync(Guid uploadFileId, Ben.Data.Common.Enums.EvidenceVoteType voteType, string? comment, CancellationToken token = default);
    Task<bool> RemoveEvidenceVoteAsync(Guid uploadFileId, CancellationToken token = default);

    // ── Messaging ─────────────────────────────────────────────────────────────

    Task<IReadOnlyList<OrgMessageRecord>> GetOrgInboxAsync(Guid orgId, CancellationToken token = default);
    Task<IReadOnlyList<OrgMessageRecord>> GetOrgSentAsync(Guid orgId, CancellationToken token = default);
    Task<OrgMessageRecord?> GetOrgMessageAsync(Guid orgId, Guid messageId, CancellationToken token = default);
    Task<OrgMessageRecord?> SendOrgMessageAsync(Guid orgId, SendOrgMessageRequest request, CancellationToken token = default);

    // ── Calendar ──────────────────────────────────────────────────────────────

    Task<IReadOnlyList<OrgCalendarEventTypeRecord>> GetCalendarEventTypesAsync(Guid orgId, CancellationToken token = default);
    Task<OrgCalendarEventTypeRecord?> CreateCalendarEventTypeAsync(Guid orgId, UpsertCalendarEventTypeRequest request, CancellationToken token = default);
    Task<OrgCalendarEventTypeRecord?> UpdateCalendarEventTypeAsync(Guid orgId, Guid id, UpsertCalendarEventTypeRequest request, CancellationToken token = default);
    Task<bool> DeleteCalendarEventTypeAsync(Guid orgId, Guid id, CancellationToken token = default);

    Task<IReadOnlyList<OrgCalendarEventRecord>> GetCalendarEventsAsync(Guid orgId, DateTime? from = null, DateTime? to = null, CancellationToken token = default);
    Task<OrgCalendarEventRecord?> GetCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default);
    Task<OrgCalendarEventRecord?> CreateCalendarEventAsync(Guid orgId, UpsertCalendarEventRequest request, CancellationToken token = default);
    Task<OrgCalendarEventRecord?> UpdateCalendarEventAsync(Guid orgId, Guid eventId, UpsertCalendarEventRequest request, CancellationToken token = default);
    Task<bool> DeleteCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    Task<IReadOnlyList<OrgCalendarEventAttendeeRecord>> GetCalendarEventAttendeesAsync(Guid orgId, Guid eventId, CancellationToken token = default);
    Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeAsync(Guid orgId, Guid eventId, AddAttendeeRequest request, CancellationToken token = default);
    Task<OrgCalendarEventAttendeeRecord?> RsvpCalendarEventAsync(Guid orgId, Guid eventId, Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus status, CancellationToken token = default);
    Task<bool> RemoveCalendarAttendeeAsync(Guid orgId, Guid eventId, Guid attendeeId, CancellationToken token = default);

    // ── Cases ─────────────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseRecord>> GetOrgCasesAsync(Guid orgId, CancellationToken token = default);
    Task<CaseRecord?> GetOrgCaseAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseRecord?> CreateOrgCaseAsync(Guid orgId, CreateCaseRequest request, CancellationToken token = default);
    Task<IReadOnlyList<OrgPendingRequestRecord>> GetOrgPendingRequestsAsync(Guid orgId, CancellationToken token = default);
    Task<CaseRecord?> AcceptClientRequestAsCaseAsync(Guid orgId, Guid clientRequestId, AcceptClientRequestAsCaseRequest request, CancellationToken token = default);
    Task<bool> DeclineClientRequestAsync(Guid orgId, Guid clientRequestId, CancellationToken token = default);
    /// <summary>Marks a pending request as Viewed or UnderReview without accepting or declining.</summary>
    Task<bool> UpdatePendingRequestStatusAsync(Guid orgId, Guid clientRequestId, Ben.Data.Common.Enums.ClientOrgRequestStatus status, CancellationToken token = default);
    Task<CaseRecord?> UpdateOrgCaseAsync(Guid orgId, Guid caseId, UpdateCaseRequest request, CancellationToken token = default);
    Task<IReadOnlyList<CaseTimelineEntryRecord>> GetCaseTimelineAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseTimelineEntryRecord?> AddCaseTimelineEntryAsync(Guid orgId, Guid caseId, UpsertTimelineEntryRequest request, CancellationToken token = default);
    Task<CaseTimelineEntryRecord?> UpdateCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, UpsertTimelineEntryRequest request, CancellationToken token = default);
    Task<bool> DeleteCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default);

    /// <summary>Returns published reports the client can view for their case.</summary>
    Task<IReadOnlyList<CaseReportSummary>> GetMyCaseReportsAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Returns a URL to stream the published report PDF for the client.</summary>
    string GetMyCaseReportPdfUrl(Guid caseId, Guid reportId);

    // ── Case Report Builder ───────────────────────────────────────────────────

    Task<IReadOnlyList<CaseReportSummary>> GetCaseReportsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseReportDetail?> GetCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);
    Task<CaseReportDetail?> CreateCaseReportAsync(Guid orgId, Guid caseId, UpsertCaseReportRequest request, CancellationToken token = default);
    Task<CaseReportDetail?> UpdateCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, UpsertCaseReportRequest request, CancellationToken token = default);
    Task<CaseReportDetail?> PublishCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);
    Task<bool> DeleteCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);
    Task<CaseReportSectionDto?> AddReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, UpsertSectionRequest request, CancellationToken token = default);
    Task<CaseReportSectionDto?> UpdateReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, UpsertSectionRequest request, CancellationToken token = default);
    Task<bool> DeleteReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, CancellationToken token = default);
    Task<CaseReportSectionFileDto?> AddReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid uploadFileId, string? caption, CancellationToken token = default);
    Task<bool> RemoveReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid fileId, CancellationToken token = default);
    /// <summary>Returns a URL to stream the PDF export for in-browser viewing.</summary>
    string GetReportPdfUrl(Guid orgId, Guid caseId, Guid reportId);

    /// <summary>Downloads the report PDF bytes using the bearer token.</summary>
    Task<(byte[] Data, string FileName)?> DownloadCaseReportPdfAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default);

    /// <summary>Downloads the published report PDF bytes for the client.</summary>
    Task<(byte[] Data, string FileName)?> DownloadMyCaseReportPdfAsync(Guid caseId, Guid reportId, CancellationToken token = default);

    // ── Case Research ─────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseResearchEntryDto>> GetCaseResearchAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseResearchEntryDto?> AddCaseResearchAsync(Guid orgId, Guid caseId, UpsertResearchRequest request, CancellationToken token = default);
    Task<CaseResearchEntryDto?> UploadCaseResearchFileAsync(Guid orgId, Guid caseId, string title, string? description, Stream content, string fileName, string contentType, CancellationToken token = default);
    Task<CaseResearchEntryDto?> UpdateCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, UpsertResearchRequest request, CancellationToken token = default);
    Task<bool> DeleteCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default);

    // ── Case Notes ────────────────────────────────────────────────────────────

    Task<IReadOnlyList<CaseNoteDto>> GetCaseNotesAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<CaseNoteDto?> CreateCaseNoteAsync(Guid orgId, Guid caseId, UpsertCaseNoteDto request, CancellationToken token = default);
    Task<CaseNoteDto?> UpdateCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, UpsertCaseNoteDto request, CancellationToken token = default);
    Task<bool> DeleteCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, CancellationToken token = default);

    // ── Investigation Scheduling ──────────────────────────────────────────────

    // Org side
    Task<IReadOnlyList<ScheduleProposalDto>> GetScheduleProposalsAsync(Guid orgId, Guid caseId, CancellationToken token = default);
    Task<ScheduleProposalDto?> CreateScheduleProposalAsync(Guid orgId, Guid caseId, CreateProposalRequest request, CancellationToken token = default);
    Task<bool> WithdrawScheduleProposalAsync(Guid orgId, Guid caseId, Guid proposalId, CancellationToken token = default);
    Task<ScheduleProposalDto?> ConvertProposalToInvestigationAsync(Guid orgId, Guid caseId, Guid proposalId, ConvertProposalRequest request, CancellationToken token = default);

    // Client side
    Task<IReadOnlyList<ScheduleProposalDto>> GetMyScheduleProposalsAsync(Guid caseId, CancellationToken token = default);
    Task<ScheduleProposalDto?> AcceptScheduleProposalAsync(Guid caseId, Guid proposalId, Guid slotId, CancellationToken token = default);
    Task<ScheduleProposalDto?> CounterScheduleProposalAsync(Guid caseId, Guid proposalId, DateTime preferredDateTime, string? notes, CancellationToken token = default);
    Task<ScheduleProposalDto?> DeclineScheduleProposalAsync(Guid caseId, Guid proposalId, string? notes, CancellationToken token = default);

    // ── Client Requests ───────────────────────────────────────────────────────

    Task<IReadOnlyList<ClientRequestRecord>> GetMyClientRequestsAsync(CancellationToken token = default);
    Task<ClientRequestRecord?> GetClientRequestAsync(Guid id, CancellationToken token = default);
    Task<IReadOnlyList<ClientRequestOrganizationRecord>> GetClientRequestOrgsAsync(Guid id, CancellationToken token = default);
    Task<ClientRequestRecord?> CreateClientRequestAsync(UpsertClientRequestRequest request, CancellationToken token = default);
    Task<ClientRequestRecord?> UpdateClientRequestAsync(Guid id, UpsertClientRequestRequest request, CancellationToken token = default);
    Task<ClientRequestRecord?> SubmitClientRequestAsync(Guid id, IList<Guid> organizationIds, CancellationToken token = default);
    Task<ClientRequestRecord?> WithdrawClientRequestAsync(Guid id, CancellationToken token = default);
    Task<ClientRequestRecord?> AddOrganizationToRequestAsync(Guid id, Guid organizationId, CancellationToken token = default);

    // ── My Cases (client dashboard) ───────────────────────────────────────────

    /// <summary>Returns all cases where the current user is the originating client.</summary>
    Task<IReadOnlyList<ClientCaseListItem>> GetMyCasesAsync(CancellationToken token = default);

    /// <summary>Returns case detail + client-visible occurrences and upcoming investigations.</summary>
    Task<ClientCaseDetail?> GetMyCaseAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Logs a new occurrence (ClientReport timeline entry) on the client's case.</summary>
    Task<CaseTimelineEntryRecord?> LogOccurrenceAsync(Guid caseId, LogOccurrenceRequest request, CancellationToken token = default);

    /// <summary>Updates a previously logged occurrence.</summary>
    Task<CaseTimelineEntryRecord?> UpdateOccurrenceAsync(Guid caseId, Guid entryId, LogOccurrenceRequest request, CancellationToken token = default);

    /// <summary>Deletes a previously logged occurrence.</summary>
    Task<bool> DeleteOccurrenceAsync(Guid caseId, Guid entryId, CancellationToken token = default);

    // ── Co-client access management ───────────────────────────────────────────

    Task<IReadOnlyList<CoClientItem>> GetCoClientsAsync(Guid caseId, CancellationToken token = default);
    Task<CoClientItem?> AddCoClientAsync(Guid caseId, string email, CancellationToken token = default);
    Task<bool> RemoveCoClientAsync(Guid caseId, Guid accessId, CancellationToken token = default);

    /// <summary>Attaches a file to an occurrence entry using case-scoped storage.</summary>
    Task<OccurrenceFileItem?> AttachOccurrenceFileAsync(Guid caseId, Guid entryId, Stream content, string fileName, string contentType, CancellationToken token = default);

    /// <summary>Removes a file attachment from an occurrence and deletes the stored file.</summary>
    Task<bool> DetachOccurrenceFileAsync(Guid caseId, Guid entryId, Guid fileId, CancellationToken token = default);

    /// <summary>Returns all case messages visible to the client (marks org messages read).</summary>
    Task<IReadOnlyList<CaseMessageRecord>> GetMyCaseMessagesAsync(Guid caseId, CancellationToken token = default);

    /// <summary>Posts a message from the client to the org on this case.</summary>
    Task<CaseMessageRecord?> PostMyCaseMessageAsync(Guid caseId, string body, CancellationToken token = default);

    /// <summary>Client cancels a scheduled investigation (422 if outside cancellation window).</summary>
    Task<bool> CancelMyInvestigationAsync(Guid caseId, Guid investigationId, CancellationToken token = default);

    // ── My Investigations (member dashboard) ──────────────────────────────────

    /// <summary>Returns all investigations the current user is assigned to attend.</summary>
    Task<IReadOnlyList<MyInvestigationItem>> GetMyInvestigationsAsync(CancellationToken token = default);

    /// <summary>Sets the current user's RSVP on their attendee record.</summary>
    Task UpdateMyInvestigationRsvpAsync(Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus rsvp, CancellationToken token = default);

    // ── Case Messages (org side) ───────────────────────────────────────────────

    /// <summary>Returns all case messages visible to the org (marks client messages read).</summary>
    Task<IReadOnlyList<CaseMessageRecord>> GetCaseMessagesAsync(Guid orgId, Guid caseId, CancellationToken token = default);

    /// <summary>Posts a message from the org to the client on this case.</summary>
    Task<CaseMessageRecord?> PostCaseMessageAsync(Guid orgId, Guid caseId, string body, CancellationToken token = default);

    /// <summary>Returns the count of unread client messages the org hasn't seen yet.</summary>
    Task<int> GetCaseMessageUnreadCountAsync(Guid orgId, Guid caseId, CancellationToken token = default);

    // ── Experience Taxonomy ───────────────────────────────────────────────────

    /// <summary>Returns all approved, active categories with their types (public — no auth).</summary>
    Task<IReadOnlyList<ExperienceCategoryWithTypesResponse>> GetExperienceTaxonomyAsync(CancellationToken token = default);

    /// <summary>SuperAdmin: all categories including pending/inactive.</summary>
    Task<IReadOnlyList<ExperienceCategoryRecord>> GetAllExperienceCategoriesAsync(CancellationToken token = default);

    /// <summary>SuperAdmin: all types for a category including pending/inactive.</summary>
    Task<IReadOnlyList<ExperienceTypeRecord>> GetAllExperienceTypesAsync(Guid categoryId, CancellationToken token = default);

    Task<ExperienceCategoryRecord?> CreateExperienceCategoryAsync(UpsertExperienceCategoryRequest request, CancellationToken token = default);
    Task<ExperienceCategoryRecord?> UpdateExperienceCategoryAsync(Guid id, UpsertExperienceCategoryRequest request, CancellationToken token = default);
    Task<bool> DeleteExperienceCategoryAsync(Guid id, CancellationToken token = default);
    Task<ExperienceCategoryRecord?> ApproveExperienceCategoryAsync(Guid id, CancellationToken token = default);

    Task<ExperienceTypeRecord?> CreateExperienceTypeAsync(Guid categoryId, UpsertExperienceTypeRequest request, CancellationToken token = default);
    Task<ExperienceTypeRecord?> UpdateExperienceTypeAsync(Guid categoryId, Guid id, UpsertExperienceTypeRequest request, CancellationToken token = default);
    Task<bool> DeleteExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default);
    Task<ExperienceTypeRecord?> ApproveExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default);

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

    // ── Audio Markers (EVP) ───────────────────────────────────────

    /// <summary>Returns all EVP markers for the given file, ordered by time.</summary>
    Task<IReadOnlyList<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default);

    /// <summary>Creates a new EVP marker and returns the persisted record.</summary>
    Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default);

    /// <summary>Updates an existing EVP marker (time, label, confidence, note).</summary>
    Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default);

    /// <summary>Permanently deletes an EVP marker.</summary>
    Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default);

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

    // ── Audio Edit (destructive) ──────────────────────────────────

    /// <summary>
    /// Applies a destructive audio edit (cut, silence, normalize, gain, fade, reverse) to
    /// <paramref name="fileId"/> and saves the result as a new UploadFile. The source is never modified.
    /// </summary>
    Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default);

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

    // ── Video projects ────────────────────────────────────────────────────────
    Task<IReadOnlyList<VideoProjectRecord>> GetMyVideoProjectsAsync(Guid? caseId = null, CancellationToken token = default);
    Task<VideoProjectRecord?> GetMyVideoProjectAsync(Guid id, CancellationToken token = default);
    Task<VideoProjectRecord?> SaveMyVideoProjectAsync(Ben.Video.Editor.Models.ProjectFile file, Guid? caseId = null, CancellationToken token = default);
    Task<VideoProjectRecord?> UpdateMyVideoProjectAsync(Guid id, Ben.Video.Editor.Models.ProjectFile file, CancellationToken token = default);
    Task<VideoProjectRecord?> PublishVideoProjectAsync(Guid id, byte[] bytes, string fileName, string contentType, CancellationToken token = default);
    Task<bool> DeleteMyVideoProjectAsync(Guid id, CancellationToken token = default);

    // ── Image editor ────────────────────────────────────────────────────────
    Task<UploadFileRecord?> SaveImageEditStateAsync(Guid fileId, string? editStateJson, CancellationToken token = default);
    Task<UploadFileRecord?> SaveImageAsNewVersionAsync(Guid parentFileId, byte[] imageBytes, string format, CancellationToken token = default);
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
    bool IsAcceptingApplications,
    bool CanEdit,
    bool CanDelete);

/// <summary>Request body for creating a new organization (SuperAdmin only).</summary>
public sealed record AdminCreateOrganizationRequest(string Name, string UrlName,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null);

/// <summary>Request body for updating an organization's Name and UrlName.</summary>
public sealed record AdminUpdateOrganizationRequest(string Name, string UrlName,
    bool IsAcceptingApplications = false,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null);

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
    IReadOnlyList<OrgPublicNavItem> NavPages,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null);

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
public sealed record OrgMembershipItem(Guid MembershipId, Guid AppUserId, OrganizationMemberRole Role, bool IsActive, string? DisplayName = null);

/// <summary>Computed display label: DisplayName → email → id.</summary>
public static class OrgMembershipItemExtensions
{
    public static string Label(this OrgMembershipItem m) =>
        string.IsNullOrWhiteSpace(m.DisplayName)
            ? $"{m.Role} ({m.MembershipId.ToString()[..8]}…)"
            : $"{m.DisplayName} ({m.Role})";
}

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

// ── Experience Taxonomy request records ──────────────────────────────────────
public sealed record UpsertExperienceCategoryRequest(
    string Name,
    string? Description,
    string? IconClass,
    string? ColorClass,
    int SortOrder,
    bool IsActive);

public sealed record UpsertExperienceTypeRequest(
    string Name,
    string? Description,
    string? IconClass,
    int SortOrder,
    bool IsActive);

public sealed record ExperienceCategoryWithTypesResponse(
    ExperienceCategoryRecord Category,
    IReadOnlyList<ExperienceTypeRecord> Types);

// ── Area of operation / org discovery records ─────────────────────────────────
public sealed record UpsertAreaOfOperationRequest(
    decimal RadiusMiles,
    decimal CenterLatitude,
    decimal CenterLongitude,
    string? DisplayLabel,
    bool IsAcceptingClients,
    bool AcceptsClientsOutsideRange);

/// <summary>
/// Public search result — center coordinates intentionally omitted for privacy.
/// </summary>
public sealed record OrgSearchResult(
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? DisplayLabel,
    double RadiusMiles,
    double DistanceFromSearchMiles,
    bool IsWithinRange,
    bool AcceptsClientsOutsideRange,
    Guid? ActiveLogoFileId);

// ── Phase 6: Case Transfer + Public Discovery records ─────────────────────────
public sealed record PublicCaseListItem(
    string CaseReference,
    string Title,
    string City,
    string State,
    Ben.Data.Common.Enums.CaseStatus Status,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    bool IsHaunted);

/// <summary>
/// Full public case detail returned by <c>GET api/public/organizations/{orgUrlName}/cases/{caseRef}</c>.
/// Consumed by <c>OrgPublicCaseDetail.razor</c>.
/// </summary>
/// <param name="CaseId">DB primary key — used by <c>CaseVoteWidget.razor</c> to fetch/cast votes.</param>
/// <param name="ClientName">
/// The client's <c>PublicPseudonym</c> when set, otherwise <c>null</c>.
/// Real names are never exposed on public endpoints.
/// </param>
/// <param name="Timeline">
/// Public timeline entries ordered by <c>EventDateTime</c>.
/// Evidence entries include <see cref="PublicTimelineEntry.EvidenceFileIds"/> for <c>EvidenceVoteWidget</c>.
/// </param>
public sealed record PublicCaseDetail(
    Guid CaseId,
    string CaseReference,
    string Title,
    string City,
    string State,
    string Country,
    Ben.Data.Common.Enums.CaseStatus Status,
    bool IsHaunted,
    string? ClientName,
    string? Description,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    IReadOnlyList<PublicTimelineEntry> Timeline,
    string OrgName,
    string OrgUrlName);

/// <summary>
/// A single public timeline entry within a <see cref="PublicCaseDetail"/>.
/// </summary>
/// <param name="EvidenceFileIds">
/// Non-empty only for <c>CaseTimelineEntryType.Evidence</c> entries.
/// Each ID is passed to <c>EvidenceVoteWidget.razor</c> so visitors can vote on individual pieces of evidence.
/// </param>
public sealed record PublicTimelineEntry(
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string? Title,
    string? Body,
    IReadOnlyList<Guid> EvidenceFileIds);

// ── Global public case discovery ──────────────────────────────────────────────

/// <summary>Paged wrapper returned by <c>GET api/public/cases</c>. Drives the home-page map and ranked list.</summary>
public sealed record PublicCaseDiscoveryPagedResponse(
    IReadOnlyList<PublicCaseDiscoveryItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// A single row in the global case discovery feed. Used by both the map markers
/// and the ranked card list in <c>PublicCaseDiscovery.razor</c>.
/// </summary>
/// <param name="CaseId">
/// DB primary key — stored on each <c>CaseMapMarker</c> so that the map popup
/// can pass it to <c>CaseVoteWidget.razor</c>.
/// </param>
/// <param name="ApproxLatitude">
/// City-level coordinates geocoded from the case's city/state/country and cached
/// 24 h in <c>IMemoryCache</c>. Null when geocoding fails.
/// Exact street addresses are never included.
/// </param>
/// <param name="ConfirmsCount">Aggregate count of <c>EvidenceVote</c> rows whose
/// type is <c>Confirms</c> across all timeline-entry files of this case.</param>
public sealed record PublicCaseDiscoveryItem(
    Guid     CaseId,
    string   CaseReference,
    string   Title,
    string   City,
    string   State,
    string   Country,
    Ben.Data.Common.Enums.CaseStatus Status,
    bool     IsHaunted,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    string   OrgName,
    string   OrgUrlName,
    int      ConfirmsCount,
    int      DisputesCount,
    int      InconclusiveCount,
    int      TotalVotes,
    decimal? ApproxLatitude,
    decimal? ApproxLongitude,
    string?  ClientName);

// ── Phase 5: Investigation + Evidence Voting request records ──────────────────
public sealed record UpsertInvestigationRequest(
    string Title,
    string? Description,
    string? Location,
    DateTime ScheduledDateTime,
    DateTime? EndDateTime,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    string? Notes,
    Guid? OrgCalendarEventId,
    DateTime? EvidenceDueDate = null);

public sealed record AddInvestigationAttendeeRequest(Guid AppUserId, string? AssignedRole);

// ── Phase 4: Messaging + Calendar request records ─────────────────────────────
public sealed record SendOrgMessageRequest(
    Ben.Data.Common.Enums.OrgMessageChannel ChannelType,
    string? Subject,
    string Body,
    bool IsEncrypted,
    Guid? ParentMessageId,
    Guid? CaseId,
    IList<Guid> RecipientUserIds);

public sealed record UpsertCalendarEventTypeRequest(
    string Name,
    string? ColorClass,
    string? IconClass,
    int SortOrder,
    bool IsActive);

public sealed record UpsertCalendarEventRequest(
    string Title,
    string? Description,
    string? Location,
    DateTime StartDateTime,
    DateTime EndDateTime,
    bool IsAllDay,
    bool IsPublic,
    Guid? EventTypeId,
    Guid? CaseId,
    string? RecurrenceRule);

public sealed record AddAttendeeRequest(Guid AppUserId, string? AssignedTask);

// ── Membership Phase 3 request records ────────────────────────────────────────
public sealed record UpsertMembershipQuestionRequest(
    string QuestionText,
    bool IsRequired,
    int SortOrder,
    bool IsActive);

// ── Case request records ──────────────────────────────────────────────────────
public sealed record CreateCaseRequest(
    string Title,
    string? Description,
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude);

public sealed record AcceptClientRequestAsCaseRequest(
    string? Title,
    Guid? CaseManagerAppUserId);

public sealed record UpdateCaseRequest(
    string? Title,
    string? Description,
    Ben.Data.Common.Enums.CaseStatus Status,
    string? PublicPseudonym,
    bool IsPublic,
    Guid? CaseManagerAppUserId);

public sealed record UpsertTimelineEntryRequest(
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string? Title,
    string? Body,
    bool IsPublic,
    IList<Guid> ExperienceTypeIds);

// ── Client Request request records ────────────────────────────────────────────
public sealed record UpsertClientRequestRequest(
    string StreetAddress1,
    string? StreetAddress2,
    string City,
    string State,
    string ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    Ben.Data.Common.Enums.ClientGender Gender,
    int? BirthYear,
    string? Description);
// ── My Cases (client dashboard) response records ─────────────────────────────
public sealed record ClientCaseListItem(
    Guid      CaseId,
    string    CaseReference,
    string    Title,
    string    City,
    string    State,
    Ben.Data.Common.Enums.CaseStatus Status,
    string?   CaseManagerDisplayName,
    DateTime  DateCaseOpened,
    DateTime? NextInvestigationDate = null);

public sealed record ClientCaseDetail(
    Guid      CaseId,
    string    CaseReference,
    string    Title,
    string    City,
    string    State,
    Ben.Data.Common.Enums.CaseStatus Status,
    string?   Description,
    string?   CaseManagerDisplayName,
    DateTime  DateCaseOpened,
    DateTime? DateCaseClosed,
    IReadOnlyList<ClientCaseOccurrence>    Occurrences,
    IReadOnlyList<ClientCaseInvestigation> Investigations,
    int       UnreadMessageCount = 0);

public sealed record ClientCaseOccurrence(
    Guid      Id,
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string?   Title,
    string?   Body,
    DateTime  DateCreated,
    IReadOnlyList<OccurrenceFileItem> Files);

public sealed record OccurrenceFileItem(
    Guid   FileId,
    string FileName,
    string ContentType,
    long   FileSize);

// ── Case Report Builder records ───────────────────────────────────────────────
public sealed record UpsertCaseReportRequest(
    string    Title,
    string?   Summary,
    string?   Conclusion,
    DateTime? ExpectedDeliveryDate);

public sealed record UpsertSectionRequest(
    string                                           Title,
    string?                                          Body,
    Ben.Data.Common.Enums.CaseReportSectionType      SectionType);

public sealed record CaseReportSummary(
    Guid                                             Id,
    Guid                                             CaseId,
    string                                           Title,
    Ben.Data.Common.Enums.CaseReportStatus           Status,
    DateTime?                                        ExpectedDeliveryDate,
    DateTime?                                        PublishedAt,
    DateTime                                         DateCreated);

public sealed record CaseReportDetail(
    Guid                                             Id,
    Guid                                             CaseId,
    string                                           Title,
    string?                                          Summary,
    string?                                          Conclusion,
    Ben.Data.Common.Enums.CaseReportStatus           Status,
    DateTime?                                        ExpectedDeliveryDate,
    DateTime?                                        PublishedAt,
    DateTime                                         DateCreated,
    IReadOnlyList<CaseReportSectionDto>              Sections);

public sealed record CaseReportSectionDto(
    Guid                                             Id,
    Guid                                             CaseReportId,
    int                                              SortOrder,
    string                                           Title,
    string?                                          Body,
    Ben.Data.Common.Enums.CaseReportSectionType      SectionType,
    IReadOnlyList<CaseReportSectionFileDto>          Files);

public sealed record CaseReportSectionFileDto(
    Guid    Id,
    Guid    UploadFileId,
    string  FileName,
    string  ContentType,
    long    FileSize,
    string? Caption,
    int     SortOrder);

public sealed record ClientCaseInvestigation(
    Guid       Id,
    string     Title,
    DateTime   ScheduledDateTime,
    DateTime?  EndDateTime,
    string?    Location,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    DateTime?  EvidenceDueDate = null,
    DateTime?  CancellationDeadlineUtc = null);

public sealed record LogOccurrenceRequest(
    DateTime? EventDateTime,
    string?   Title,
    string?   Body);

// ── Case message board response records ──────────────────────────────────────
public sealed record CaseMessageRecord(
    Guid                             Id,
    Guid                             CaseId,
    Guid                             AuthorAppUserId,
    string                           AuthorDisplayName,
    string                           Body,
    Ben.Data.Common.Enums.CaseMessageSide SenderSide,
    bool                             IsReadByClient,
    bool                             IsReadByOrg,
    DateTime                         DateCreated);

// ── My Investigations response records ───────────────────────────────────────
public sealed record MyInvestigationItem(
    Guid                               AttendeeId,
    Guid                               InvestigationId,
    Guid                               CaseId,
    string                             CaseReference,
    string                             CaseTitle,
    Guid                               OrgId,
    string                             OrgName,
    string                             OrgUrlName,
    string                             Title,
    DateTime                           ScheduledDateTime,
    DateTime?                          EndDateTime,
    string?                            Location,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    string?                            AssignedRole,
    Ben.Data.Common.Enums.RsvpStatus   Rsvp,
    bool?                              DidAttend,
    DateTime?                          EvidenceDueDate);

// ── Case Research records ─────────────────────────────────────────────────────
public sealed record UpsertResearchRequest(
    Ben.Data.Common.Enums.CaseResearchType ResearchType,
    string  Title,
    string? Body,
    string? Url);

public sealed record CaseResearchEntryDto(
    Guid                                   Id,
    Guid                                   CaseId,
    Ben.Data.Common.Enums.CaseResearchType ResearchType,
    string                                 Title,
    string?                                Body,
    string?                                Url,
    ResearchFileInfo?                      File,
    int                                    SortOrder,
    DateTime                               DateCreated);

public sealed record ResearchFileInfo(Guid FileId, string FileName, string ContentType, long FileSize);

// ── Investigation Scheduling records ─────────────────────────────────────────
public sealed record CreateProposalRequest(string? Notes, IReadOnlyList<SlotInput> Slots);
public sealed record SlotInput(DateTime StartDateTime, DateTime? EndDateTime);
public sealed record ConvertProposalRequest(Guid? SlotId, string? Title);

public sealed record ScheduleProposalDto(
    Guid                                              Id,
    Guid                                              CaseId,
    Ben.Data.Common.Enums.ScheduleProposalStatus      Status,
    string?                                           Notes,
    Guid?                                             AcceptedSlotId,
    DateTime?                                         ClientCounterDateTime,
    string?                                           ClientResponseNotes,
    DateTime?                                         ClientRespondedAt,
    Guid?                                             InvestigationId,
    DateTime                                          DateCreated,
    IReadOnlyList<SlotDto>                            Slots);

public sealed record SlotDto(Guid Id, DateTime StartDateTime, DateTime? EndDateTime, int SortOrder);

// ── Co-client access records ──────────────────────────────────────────────────
public sealed record CoClientItem(Guid AccessId, Guid AppUserId, string DisplayName);

// ── Case note records ─────────────────────────────────────────────────────────
public sealed record CaseNoteDto(
    Guid      Id,
    Guid      CaseId,
    Guid      AuthorAppUserId,
    string?   AuthorDisplayName,
    string?   Title,
    string    Body,
    bool      IsPinned,
    DateTime  DateCreated,
    DateTime? DateUpdated);

public sealed record UpsertCaseNoteDto(string? Title, string Body, bool IsPinned = false);
