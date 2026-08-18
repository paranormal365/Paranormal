using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// The Organization slice of <see cref="IBenAdminClient"/> — organizations, their addresses, files, roles, logos and settings.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenOrganizationClient
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

    // ── Cross-org visibility (SuperAdmin) ────────────────────────────────────

    /// <summary>Returns every case across every organization (SuperAdmin only).</summary>
    Task<IReadOnlyList<AdminCaseSummaryRecord>> GetAllCasesAsync(CancellationToken token = default);

    /// <summary>Returns every investigation across every organization (SuperAdmin only).</summary>
    Task<IReadOnlyList<AdminInvestigationSummaryRecord>> GetAllInvestigationsAsync(CancellationToken token = default);

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

    /// <summary>
    /// Every organization, paged, with no location required — what the "Browse All Groups"
    /// entry point needs. Anonymous, like the proximity search beside it.
    /// </summary>
    Task<OrgBrowsePage?> BrowseOrganizationsAsync(int page = 1, int pageSize = 24, CancellationToken token = default);

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

    /// <summary>
    /// One of the group's CMS pages as a visitor would see it, whether or not it is published.
    /// Returns the same shape as <see cref="GetPublicOrgPageAsync"/> so one renderer draws both and
    /// a preview cannot drift from the real page.
    /// </summary>
    Task<OrgPublicPageResponse?> GetCmsPagePreviewAsync(Guid orgId, Guid pageId, CancellationToken token = default);
    string GetFileDownloadUrl(Guid uploadFileId);
    string GetOrgFileDownloadUrl(Guid orgId, Guid orgFileId);

    /// <summary>
    /// Where a visitor fetches one published file of a case — the only anonymous route to a case's
    /// media, gated per request by the publication rule.
    /// </summary>
    /// <remarks>
    /// Used by the public renderer <b>and</b> by the editor's picker, deliberately. An author
    /// choosing photos sees them through exactly the pipe a visitor will, so a file that would
    /// arrive broken on the public page arrives broken in the editor too, while somebody is still
    /// looking. The ordinary download URL would have shown the author a picture nobody else could
    /// see.
    /// </remarks>
    string GetPublicCaseMediaUrl(Guid caseId, Guid uploadFileId);

    /// <summary>
    /// The prefix the public renderer appends <c>{caseId}/media/{fileId}</c> to.
    /// </summary>
    /// <remarks>
    /// Its own method rather than the substring-of-a-built-URL trick the file-download base uses
    /// beside it. That trick relies on replacing an empty GUID out of a formatted string, which is
    /// unreadable at the call site and — with two GUIDs in this route — needs quotes that a Razor
    /// attribute cannot carry. Asking the client for what it actually knows is shorter and cannot
    /// be got subtly wrong.
    /// </remarks>
    string GetPublicCaseMediaBaseUrl();

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

    // ── Org address member access ──────────────────────────────────────────────
    Task<IReadOnlyList<OrganizationAddressMemberAccessRecord>> GetAddressMemberAccessAsync(Guid orgId, Guid addressId, CancellationToken token = default);
    Task<OrganizationAddressMemberAccessRecord?> AddAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid orgUserMembershipId, CancellationToken token = default);
    Task<bool> RemoveAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid accessId, CancellationToken token = default);

    // ── Org settings ──────────────────────────────────────────────────────────
    Task<OrgSettingsResponse?> GetOrgSettingsAsync(Guid orgId, CancellationToken token = default);
    Task<OrgSettingsResponse?> UpdateOrgSettingsAsync(Guid orgId, OrgSettingsRequest request, CancellationToken token = default);

    // ── Public events (item #87) ────────────────────────────────────────────

    /// <summary>Upcoming public events, across every organization or narrowed to one.</summary>
    Task<IReadOnlyList<PublicEventListItem>> GetPublicEventsAsync(string? orgUrlName = null, int maxResults = 50, CancellationToken token = default);

    /// <summary>One public event by its readable URL.</summary>
    Task<PublicEventRecord?> GetPublicEventAsync(string orgUrlName, string eventSlug, CancellationToken token = default);

    /// <summary>Public events this caller has said they are coming to, recent past included.</summary>
    Task<IReadOnlyList<PublicEventListItem>> GetMyPublicEventsAsync(CancellationToken token = default);

    /// <summary>Says the signed-in caller is coming. Returns the refreshed event.</summary>
    Task<PublicEventRecord?> RsvpToEventAsync(Guid eventId, CancellationToken token = default);

    /// <summary>
    /// Asks to attend by email, for somebody who is not signed in. Always succeeds from the
    /// caller's point of view — a different answer for a known address would let anyone test which
    /// emails have accounts here.
    /// </summary>
    Task<bool> RequestEventAttendanceAsync(Guid eventId, string email, string? displayName, CancellationToken token = default);

    /// <summary>What a confirmation link points at, before it is used.</summary>
    Task<EventAttendanceInviteInfo?> GetEventAttendanceInviteAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Uses a confirmation link. Creates a passwordless account if there is not one.</summary>
    Task<EventAttendanceConfirmation?> ConfirmEventAttendanceAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Says they are no longer coming.</summary>
    Task<bool> CancelEventRsvpAsync(Guid eventId, CancellationToken token = default);
}
