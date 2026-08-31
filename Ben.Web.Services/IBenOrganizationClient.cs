using Ben.Web.Services.WebApi;
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
    Task<LoadResult<OrganizationListItemResponse>> GetOrganizationsAsync(CancellationToken token = default);

    /// <summary>Founds a group with the caller as Owner — the self-serve door, any signed-in user.</summary>
    Task<OrganizationSummaryResponse?> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken token = default);

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
    Task<LoadResult<AdminRoleWithCountResponse>> GetRolesAsync(CancellationToken token = default);

    /// <summary>Creates a new site-level role.</summary>
    Task<AppRoleAdminRecord?> CreateRoleAsync(string roleName, CancellationToken token = default);

    /// <summary>Deletes a role. Will fail if any users are still assigned to it.</summary>
    Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken token = default);

    // ── Cross-org visibility (SuperAdmin) ────────────────────────────────────

    /// <summary>Returns every case across every organization (SuperAdmin only).</summary>
    Task<LoadResult<AdminCaseSummaryRecord>> GetAllCasesAsync(CancellationToken token = default);

    /// <summary>Returns every investigation across every organization (SuperAdmin only).</summary>
    Task<LoadResult<AdminInvestigationSummaryRecord>> GetAllInvestigationsAsync(CancellationToken token = default);

    // ── Organization Logos ────────────────────────────────────────────────────

    Task<LoadResult<OrganizationLogoRecord>> GetOrgLogosAsync(Guid orgId, CancellationToken token = default);
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
    Task<LoadResult<OrgSearchResult>> SearchOrganizationsAsync(double lat, double lon, int maxResults = 20, CancellationToken token = default);

    /// <summary>
    /// Every organization, paged, with no location required — what the "Browse All Groups"
    /// entry point needs. Anonymous, like the proximity search beside it.
    /// </summary>
    /// <param name="toursOnly">Narrow to groups that run public walking tours (2026-08-24).</param>
    Task<OrgBrowsePage?> BrowseOrganizationsAsync(int page = 1, int pageSize = 24,
        CancellationToken token = default, bool toursOnly = false);

    // ── Organization Addresses ────────────────────────────────────────────────

    Task<LoadResult<OrganizationAddressRecord>> GetOrgAddressesAsync(Guid orgId, CancellationToken token = default);
    Task<LoadResult<OrganizationAddressTypeRecord>> GetOrgAddressTypesAsync(CancellationToken token = default);
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

    /// <summary>
    /// Where to fetch a small copy of an image file — use this for anything a person looks at.
    /// </summary>
    /// <remarks>
    /// <c>GetFileDownloadUrl</c> serves the original bytes, so an <c>&lt;img&gt;</c> pointed at it
    /// pulls a whole upload down the wire to draw a 40px avatar. Same access rules either way; a
    /// non-image falls through to the real file.
    /// </remarks>
    string GetFileThumbnailUrl(Guid uploadFileId);
    /// <summary>The approved-only public route for an ad's picture (item 166 W3).</summary>
    string GetPromotedAdImageUrl(Guid adId);
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

    /// <summary>Absolute URL for one accepted evidence submission's bytes — the API origin, not
    /// the site's, which is the trap every raw /api href falls into on the split deployment.</summary>
    string GetEventEvidenceFileUrl(Guid eventId, Guid submissionId);

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


    /// <summary>
    /// The group's roster, distinguishing "could not load" from "nobody is in it".
    /// </summary>
    /// <remarks>
    /// Prefer this for anything a person sees. The Members surfaces told readers their group was
    /// empty when the truth was a refusal, twice (items 119, 122).
    /// </remarks>
    Task<WebApi.LoadResult<OrgMembershipItem>> GetOrganizationMembersAsync(Guid orgId, CancellationToken token = default);

    Task<WebApi.LoadResult<MyMembershipOrgItem>> GetMyMembershipOrganizationsAsync(CancellationToken token = default);
    Task<WebApi.LoadResult<OrgActionNeededItem>> GetActionNeededAsync(CancellationToken token = default);
    Task<WebApi.LoadResult<ShareableUserFileItem>> GetShareableUserFilesAsync(Guid orgId, CancellationToken token = default);

    // ── Group ads (item 166 W3) ──────────────────────────────────────────────
    Task<WebApi.LoadResult<OrganizationAdRecord>> GetOrgAdsAsync(Guid orgId, CancellationToken token = default);
    Task<(OrganizationAdRecord? Result, string? Error)> CreateOrgAdAsync(Guid orgId, SaveOrganizationAdRequest request, CancellationToken token = default);
    Task<(OrganizationAdRecord? Result, string? Error)> UpdateOrgAdAsync(Guid orgId, Guid adId, SaveOrganizationAdRequest request, CancellationToken token = default);
    Task<(OrganizationAdRecord? Result, string? Error)> SubmitOrgAdAsync(Guid orgId, Guid adId, CancellationToken token = default);
    Task<(OrganizationAdRecord? Result, string? Error)> WithdrawOrgAdAsync(Guid orgId, Guid adId, CancellationToken token = default);
    Task<bool> DeleteOrgAdAsync(Guid orgId, Guid adId, CancellationToken token = default);
    Task<MyOrgPermissionsItem?> GetMyOrgPermissionsAsync(Guid orgId, CancellationToken token = default);
    Task<OrgIncludedAreasItem?> GetOrgIncludedAreasAsync(Guid orgId, CancellationToken token = default);

    // ── Member-title ladder (item 157) — seniority, never permission ─────────
    Task<WebApi.LoadResult<OrgMemberLevelItem>> GetMemberLevelsAsync(Guid orgId, CancellationToken token = default);
    Task<OrgMemberLevelItem?> CreateMemberLevelAsync(Guid orgId, string name, int sortOrder, bool isActive, CancellationToken token = default);
    Task<OrgMemberLevelItem?> UpdateMemberLevelAsync(Guid orgId, Guid levelId, string name, int sortOrder, bool isActive, CancellationToken token = default);
    Task<bool> DeleteMemberLevelAsync(Guid orgId, Guid levelId, CancellationToken token = default);
    /// <summary>
    /// Sets a member's title, optionally also granting the roles that title suggests.
    /// </summary>
    /// <remarks>
    /// <paramref name="applySuggestedRoles"/> defaults to false so a plain title change stays a
    /// plain title change. When true the grant is additive only — clearing or lowering a title
    /// never takes a role away, because access is removed on the roles screen, deliberately.
    /// </remarks>
    Task<bool> AssignMemberLevelAsync(Guid orgId, Guid membershipId, Guid? levelId,
        bool applySuggestedRoles = false, CancellationToken token = default);

    /// <summary>The roles a title suggests — what assigning it will offer to grant.</summary>
    Task<LoadResult<Guid>> GetSuggestedRolesAsync(Guid orgId, Guid levelId, CancellationToken token = default);

    /// <summary>
    /// Replaces the roles a title suggests. Changes nothing about who may do what today.
    /// </summary>
    Task<bool> SetSuggestedRolesAsync(Guid orgId, Guid levelId, IReadOnlyList<Guid> roleIds,
        CancellationToken token = default);
    Task<LoadResult<OrgMemberGroupRecord>> GetGroupsAsync(Guid orgId, CancellationToken token = default);

    // ── Organization Files ────────────────────────────────────────────────────

    /// <summary>Returns all files owned by the organization.</summary>

    /// <summary>
    /// The group's files, distinguishing "could not load" from "there are none".
    /// </summary>
    /// <remarks>
    /// Prefer this over <see cref="GetOrgFilesAsync"/> for anything a person sees. This is the
    /// surface where a refusal rendering as an empty list was actually caught — a member with a
    /// group handbook on the server was told the group had no files (items 119 and 120).
    /// </remarks>
    Task<WebApi.LoadResult<OrganizationFileRecord>> GetOrgFilesAsync(Guid orgId, CancellationToken token = default);

    /// <summary>Returns the deletion audit log for organization files.</summary>
    Task<LoadResult<OrganizationFileDeleteLogRecord>> GetOrgFileDeleteLogAsync(Guid orgId, CancellationToken token = default);

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
    Task<LoadResult<OrgMemberGroupMembershipRecord>> GetGroupMembersAsync(Guid orgId, Guid groupId, CancellationToken token = default);
    Task<OrgMemberGroupMembershipRecord?> AddGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default);
    Task<bool> RemoveGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default);

    // ── Organization Roles ────────────────────────────────────────────────────────
    Task<LoadResult<OrganizationRoleRecord>> GetOrgRolesAsync(Guid orgId, CancellationToken token = default);
    Task<OrganizationRoleRecord?> GetOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<OrganizationRoleRecord?> CreateOrgRoleAsync(Guid orgId, CreateOrgRoleRequest request, CancellationToken token = default);
    Task<OrganizationRoleRecord?> UpdateOrgRoleAsync(Guid orgId, Guid roleId, UpdateOrgRoleRequest request, CancellationToken token = default);
    Task<bool> DeleteOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<LoadResult<OrganizationRolePermissionRecord>> GetOrgRolePermissionsAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<bool> SetOrgRolePermissionsAsync(Guid orgId, Guid roleId, IEnumerable<SetRolePermissionRequest> permissions, CancellationToken token = default);
    Task<LoadResult<OrganizationRoleMembershipRecord>> GetOrgRoleMembersAsync(Guid orgId, Guid roleId, CancellationToken token = default);
    Task<OrganizationRoleMembershipRecord?> AddOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid orgUserMembershipId, CancellationToken token = default);
    Task<bool> RemoveOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid membershipId, CancellationToken token = default);

    // ── Org address member access ──────────────────────────────────────────────
    Task<LoadResult<OrganizationAddressMemberAccessRecord>> GetAddressMemberAccessAsync(Guid orgId, Guid addressId, CancellationToken token = default);
    Task<OrganizationAddressMemberAccessRecord?> AddAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid orgUserMembershipId, CancellationToken token = default);
    Task<bool> RemoveAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid accessId, CancellationToken token = default);

    // ── Org settings ──────────────────────────────────────────────────────────
    Task<OrgSettingsResponse?> GetOrgSettingsAsync(Guid orgId, CancellationToken token = default);
    Task<OrgSettingsResponse?> UpdateOrgSettingsAsync(Guid orgId, OrgSettingsRequest request, CancellationToken token = default);

    // ── Public events (item #87) ────────────────────────────────────────────

    /// <summary>Upcoming public events, across every organization or narrowed to one.</summary>
    Task<LoadResult<PublicEventListItem>> GetPublicEventsAsync(string? orgUrlName = null, int maxResults = 50, CancellationToken token = default);

    /// <summary>One public event by its readable URL.</summary>
    Task<PublicEventRecord?> GetPublicEventAsync(string orgUrlName, string eventSlug, CancellationToken token = default);

    // ── Item 111: attendee evidence at public events ──────────────────────────

    /// <summary>Offers one file to the event's record. Null error on success.</summary>
    Task<(EventEvidenceRecord? Result, string? Error)> SubmitEventEvidenceAsync(
        Guid eventId, Stream content, string fileName, string contentType, string? note, CancellationToken token = default);

    /// <summary>The caller's own submissions for an event, with review state.</summary>
    Task<LoadResult<EventEvidenceRecord>> GetMyEventEvidenceAsync(Guid eventId, CancellationToken token = default);

    /// <summary>
    /// Everything this account has ever offered, across every event — the guest's own copy.
    /// </summary>
    Task<LoadResult<EventEvidenceRecord>> GetMyEvidenceEverywhereAsync(CancellationToken token = default);

    /// <summary>Contributes one submission to the archive of the place its event was held at.</summary>
    Task<(bool Ok, string? Error)> PublishEvidenceToPlaceAsync(
        Guid eventId, Guid submissionId, CancellationToken token = default);

    /// <summary>Takes it back off the place's archive. Paid, as retraction is for field sessions.</summary>
    Task<(bool Ok, string? Error)> RetractEvidenceFromPlaceAsync(
        Guid eventId, Guid submissionId, CancellationToken token = default);

    /// <summary>Accepted evidence — the public half of the record. Anonymous.</summary>
    Task<LoadResult<EventEvidenceRecord>> GetAcceptedEventEvidenceAsync(Guid eventId, CancellationToken token = default);

    /// <summary>Submissions waiting on this group's answer.</summary>
    Task<LoadResult<EventEvidenceRecord>> GetEvidenceQueueAsync(Guid orgId, CancellationToken token = default);

    /// <summary>A member's verdict. Null error on success.</summary>
    Task<(EventEvidenceRecord? Result, string? Error)> ReviewEventEvidenceAsync(
        Guid orgId, Guid submissionId, bool accept, string? reason, CancellationToken token = default);

    /// <summary>Public events this caller has said they are coming to, recent past included.</summary>
    Task<LoadResult<PublicEventListItem>> GetMyPublicEventsAsync(CancellationToken token = default);

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
