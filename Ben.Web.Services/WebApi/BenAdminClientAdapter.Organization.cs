using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Organization half of the adapter — implements <see cref="Ben.Web.Services.IBenOrganizationClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Organizations ─────────────────────────────────────────────────────────

    public async Task<LoadResult<OrganizationListItemResponse>> GetOrganizationsAsync(CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationListItemResponse>("/api/organizations", token);
        return result;
    }

    public Task<OrganizationSummaryResponse?> RegisterOrganizationAsync(RegisterOrganizationRequest request, CancellationToken token = default)
        => _api.RegisterOrganizationAsync(request, token);

    public Task<OrganizationAdminRecord?> GetOrganizationAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<OrganizationAdminRecord>($"/api/organizations/{id}", token);

    public Task<OrganizationAdminRecord?> CreateOrganizationAsync(AdminCreateOrganizationRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateOrganizationRequest, OrganizationAdminRecord>("/api/organizations", request, token);

    public Task<OrganizationAdminRecord?> UpdateOrganizationAsync(Guid id, AdminUpdateOrganizationRequest request, CancellationToken token = default)
        => _api.PutAsync<AdminUpdateOrganizationRequest, OrganizationAdminRecord>($"/api/organizations/{id}", request, token);

    public Task<bool> DeleteOrganizationAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{id}", token);

    // ── Roles ─────────────────────────────────────────────────────────────────

    public async Task<LoadResult<AdminRoleWithCountResponse>> GetRolesAsync(CancellationToken token = default)
    {
        var result = await _api.GetListAsync<AdminRoleWithCountResponse>("/api/admin/roles", token);
        return result;
    }

    public Task<AppRoleAdminRecord?> CreateRoleAsync(string roleName, CancellationToken token = default)
        => _api.PostAsync<object, AppRoleAdminRecord>("/api/admin/roles", new { Name = roleName }, token);

    public Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/roles/{roleId}", token);

    // ── Cross-org visibility (SuperAdmin) ────────────────────────────────────

    public async Task<LoadResult<AdminCaseSummaryRecord>> GetAllCasesAsync(CancellationToken token = default)
        => await _api.GetListAsync<AdminCaseSummaryRecord>("/api/admin/cases", token);

    public async Task<LoadResult<AdminInvestigationSummaryRecord>> GetAllInvestigationsAsync(CancellationToken token = default)
        => await _api.GetListAsync<AdminInvestigationSummaryRecord>("/api/admin/investigations", token);

    // ── Organization Logos ────────────────────────────────────────────────────

    public async Task<LoadResult<OrganizationLogoRecord>> GetOrgLogosAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationLogoRecord>($"/api/organizations/{orgId}/logos", token);
        return result;
    }

    public Task<OrganizationLogoRecord?> CreateOrgLogoAsync(Guid orgId, CmsCreateLogoRequest request, CancellationToken token = default)
        => _api.PostAsync<CmsCreateLogoRequest, OrganizationLogoRecord>($"/api/organizations/{orgId}/logos", request, token);

    public Task<OrganizationLogoRecord?> UpdateOrgLogoAsync(Guid orgId, Guid logoId, CmsUpdateLogoRequest request, CancellationToken token = default)
        => _api.PutAsync<CmsUpdateLogoRequest, OrganizationLogoRecord>($"/api/organizations/{orgId}/logos/{logoId}", request, token);

    public Task<bool> DeleteOrgLogoAsync(Guid orgId, Guid logoId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/logos/{logoId}", token);

    // ── Organization Area of Operation ────────────────────────────────────────

    public Task<OrganizationAreaOfOperationRecord?> GetOrgAreaOfOperationAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<OrganizationAreaOfOperationRecord>($"/api/organizations/{orgId}/area-of-operation", token);

    public Task<OrganizationAreaOfOperationRecord?> UpsertOrgAreaOfOperationAsync(Guid orgId, UpsertAreaOfOperationRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertAreaOfOperationRequest, OrganizationAreaOfOperationRecord>($"/api/organizations/{orgId}/area-of-operation", request, token);

    public Task<bool> DeleteOrgAreaOfOperationAsync(Guid orgId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/area-of-operation", token);

    public Task<bool> UpdateClientAcceptanceAsync(Guid orgId, bool isAcceptingClients, bool acceptsClientsOutsideRange, CancellationToken token = default)
        => _api.PutVoidAsync($"/api/organizations/{orgId}/area-of-operation/acceptance",
               new { IsAcceptingClients = isAcceptingClients, AcceptsClientsOutsideRange = acceptsClientsOutsideRange }, token);

    public async Task<LoadResult<OrgSearchResult>> SearchOrganizationsAsync(double lat, double lon, int maxResults = 20, CancellationToken token = default)
    {
        var result = await _api.GetAnonymousListAsync<OrgSearchResult>(
            $"/api/public/organizations/search?lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&maxResults={maxResults}", token);
        return result;
    }

    public Task<OrgBrowsePage?> BrowseOrganizationsAsync(int page = 1, int pageSize = 24, CancellationToken token = default)
        => _api.GetAnonymousAsync<OrgBrowsePage>(
               $"/api/public/organizations/browse?page={page}&pageSize={pageSize}", token);

    // ── Organization Addresses ────────────────────────────────────────────────

    public async Task<LoadResult<OrganizationAddressRecord>> GetOrgAddressesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationAddressRecord>($"/api/organizations/{orgId}/addresses", token);
        return result;
    }

    public async Task<LoadResult<OrganizationAddressTypeRecord>> GetOrgAddressTypesAsync(CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationAddressTypeRecord>("/api/organization-address-types", token);
        return result;
    }

    public Task<OrganizationAddressRecord?> CreateOrgAddressAsync(Guid orgId, OrgAddressUpsertRequest request, CancellationToken token = default)
        => _api.PostAsync<OrgAddressUpsertRequest, OrganizationAddressRecord>($"/api/organizations/{orgId}/addresses", request, token);

    public Task<OrganizationAddressRecord?> UpdateOrgAddressAsync(Guid orgId, Guid addressId, OrgAddressUpsertRequest request, CancellationToken token = default)
        => _api.PutAsync<OrgAddressUpsertRequest, OrganizationAddressRecord>($"/api/organizations/{orgId}/addresses/{addressId}", request, token);

    public Task<bool> DeleteOrgAddressAsync(Guid orgId, Guid addressId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/addresses/{addressId}", token);

    public Task<GeocodingPreviewResponse?> PreviewGeocodingAsync(
        string streetAddress1, string? streetAddress2, string city,
        string state, string zipCode, string country, CancellationToken token = default)
    {
        var qs = $"?streetAddress1={Uri.EscapeDataString(streetAddress1)}" +
                 (streetAddress2 is not null ? $"&streetAddress2={Uri.EscapeDataString(streetAddress2)}" : "") +
                 $"&city={Uri.EscapeDataString(city)}&state={Uri.EscapeDataString(state)}" +
                 $"&zipCode={Uri.EscapeDataString(zipCode)}&country={Uri.EscapeDataString(country)}";
        return _api.GetAsync<GeocodingPreviewResponse>($"/api/geocode/preview{qs}", token);
    }

    public Task<GeocodingPreviewResponse?> SearchGeocodingAsync(string query, CancellationToken token = default)
        => _api.GetAnonymousAsync<GeocodingPreviewResponse>($"/api/geocode/search?q={Uri.EscapeDataString(query)}", token);

    public Task<ReverseGeocodingResponse?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken token = default)
    {
        var lat = latitude.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        var lon = longitude.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        return _api.GetAsync<ReverseGeocodingResponse>($"/api/geocode/reverse?latitude={lat}&longitude={lon}", token);
    }

    // ── Public Org Pages (no auth required) ───────────────────────────────────

    public Task<OrgPublicHomeResponse?> GetPublicOrgAsync(string urlName, CancellationToken token = default)
        => _api.GetAnonymousAsync<OrgPublicHomeResponse>($"/api/public/organizations/{Uri.EscapeDataString(urlName)}", token);

    public Task<OrgPublicPageResponse?> GetPublicOrgPageAsync(string urlName, string pageSlug, CancellationToken token = default)
        => _api.GetAnonymousAsync<OrgPublicPageResponse>($"/api/public/organizations/{Uri.EscapeDataString(urlName)}/pages/{Uri.EscapeDataString(pageSlug)}", token);

    // Authenticated, unlike its public twin: a preview shows unpublished work, so the server has to
    // know who is asking.
    public Task<OrgPublicPageResponse?> GetCmsPagePreviewAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.GetAsync<OrgPublicPageResponse>($"/api/organizations/{orgId}/cms/pages/{pageId}/preview", token);

    // ── Public events (item #87) ────────────────────────────────────────────

    public async Task<LoadResult<PublicEventListItem>> GetPublicEventsAsync(
        string? orgUrlName = null, int maxResults = 50, CancellationToken token = default)
    {
        var url = $"/api/public/events?maxResults={maxResults}"
                + (string.IsNullOrWhiteSpace(orgUrlName) ? "" : $"&orgUrlName={Uri.EscapeDataString(orgUrlName)}");
        var result = await _api.GetAnonymousListAsync<PublicEventListItem>(url, token);
        return result;
    }

    // Readable by a visitor who has never signed in — but the response also carries the viewer's
    // own RSVP and whether they organise the event, and fetching it anonymously meant a signed-in
    // visitor never saw either. GetAsync attaches a token only when there is one, so the anonymous
    // case is unchanged.
    public async Task<(EventEvidenceRecord? Result, string? Error)> SubmitEventEvidenceAsync(
        Guid eventId, Stream content, string fileName, string contentType, string? note, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        if (note is not null) form.Add(new StringContent(note), "note");
        using var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);

        return await _api.PostMultipartExpectingReasonAsync<EventEvidenceRecord>(
            $"/api/events/{eventId}/evidence", form, token);
    }

    public Task<LoadResult<EventEvidenceRecord>> GetMyEventEvidenceAsync(Guid eventId, CancellationToken token = default)
        => _api.GetListAsync<EventEvidenceRecord>($"/api/events/{eventId}/evidence/mine", token);

    public Task<LoadResult<EventEvidenceRecord>> GetAcceptedEventEvidenceAsync(Guid eventId, CancellationToken token = default)
        => _api.GetAnonymousListAsync<EventEvidenceRecord>($"/api/events/{eventId}/evidence/accepted", token);

    public Task<LoadResult<EventEvidenceRecord>> GetEvidenceQueueAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<EventEvidenceRecord>($"/api/organizations/{orgId}/evidence-submissions", token);

    public Task<(EventEvidenceRecord? Result, string? Error)> ReviewEventEvidenceAsync(
        Guid orgId, Guid submissionId, bool accept, string? reason, CancellationToken token = default)
        => _api.SendExpectingReasonAsync<object, EventEvidenceRecord>(
            HttpMethod.Put, $"/api/organizations/{orgId}/evidence-submissions/{submissionId}/review",
            new { accept, reason }, token);

    public Task<PublicEventRecord?> GetPublicEventAsync(string orgUrlName, string eventSlug, CancellationToken token = default)
        => _api.GetAsync<PublicEventRecord>(
               $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/events/{Uri.EscapeDataString(eventSlug)}", token);

    public async Task<LoadResult<PublicEventListItem>> GetMyPublicEventsAsync(CancellationToken token = default)
    {
        var result = await _api.GetListAsync<PublicEventListItem>("/api/public/events/mine", token);
        return result;
    }

    public Task<PublicEventRecord?> RsvpToEventAsync(Guid eventId, CancellationToken token = default)
        => _api.PostAsync<object, PublicEventRecord>($"/api/public/events/{eventId}/rsvp", new object(), token);

    public Task<bool> RequestEventAttendanceAsync(Guid eventId, string email, string? displayName, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync($"/api/public/event-attendance/{eventId}/request",
               new RequestEventAttendanceRequest(email, displayName), token);

    public Task<EventAttendanceInviteInfo?> GetEventAttendanceInviteAsync(string token, CancellationToken cancellationToken = default)
        => _api.GetAnonymousAsync<EventAttendanceInviteInfo>(
               $"/api/public/event-attendance/{Uri.EscapeDataString(token)}", cancellationToken);

    public Task<EventAttendanceConfirmation?> ConfirmEventAttendanceAsync(string token, CancellationToken cancellationToken = default)
        => _api.PostAnonymousAsync<object, EventAttendanceConfirmation>(
               $"/api/public/event-attendance/{Uri.EscapeDataString(token)}/confirm", new object(), cancellationToken);

    public Task<bool> CancelEventRsvpAsync(Guid eventId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/public/events/{eventId}/rsvp", token);

    public string GetFileDownloadUrl(Guid uploadFileId)
        => $"{_webApiBaseUrl}/api/upload-files/{uploadFileId}/download";

    public string GetFileThumbnailUrl(Guid uploadFileId)
        => $"{_webApiBaseUrl}/api/upload-files/{uploadFileId}/thumbnail";
    public string GetOrgFileDownloadUrl(Guid orgId, Guid orgFileId)
        => $"{_webApiBaseUrl}/api/organizations/{orgId}/files/{orgFileId}/download";

    public string GetEventEvidenceFileUrl(Guid eventId, Guid submissionId)
        => $"{_webApiBaseUrl}/api/events/{eventId}/evidence/{submissionId}/file";

    public string GetPublicCaseMediaUrl(Guid caseId, Guid uploadFileId)
        => $"{GetPublicCaseMediaBaseUrl()}{caseId}/media/{uploadFileId}";

    public string GetPublicCaseMediaBaseUrl()
        => $"{_webApiBaseUrl}/api/public/cases/";

    public Task<AddressMapConfigRecord?> GetOrgAddressMapConfigAsync(Guid orgId, Guid addressId, CancellationToken token = default)
        => _api.GetAsync<AddressMapConfigRecord>($"/api/organizations/{orgId}/addresses/{addressId}/map-config", token);

    public Task<AddressMapConfigRecord?> UpsertOrgAddressMapConfigAsync(Guid orgId, Guid addressId, AddressMapConfigRecord config, CancellationToken token = default)
        => _api.PutAsync<object, AddressMapConfigRecord>(
               $"/api/organizations/{orgId}/addresses/{addressId}/map-config",
               new { config.IsOnMap, config.ShowMarker, config.ShowRegion, config.RegionRadiusMiles,
                     config.MarkerColor, config.MarkerIconKey, config.RegionFillColor,
                     config.RegionFillOpacity, config.RegionStrokeColor, config.RegionStrokeOpacity,
                     config.RegionStrokeWidth }, token);

    public Task<bool> DeleteOrgAddressMapConfigAsync(Guid orgId, Guid addressId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/addresses/{addressId}/map-config", token);

    // ── Org Member Groups ─────────────────────────────────────────────────────

    /// <summary>
    /// The group's roster, saying so when it could not be fetched.
    /// </summary>
    /// <remarks>
    /// The second surface where a refusal rendered as an empty list — the Members tab told an
    /// ordinary member their group had nobody in it while Details counted three (items 119, 122).
    /// </remarks>
    public async Task<LoadResult<OrgMembershipItem>> GetOrganizationMembersAsync(Guid orgId, CancellationToken token = default)
    {
        // Map rather than Failure(Reason) + Ok(Select(…)): the hand-rolled version carried the
        // reason across but dropped SessionExpired, so a signed-out roster asked the reader to
        // "try again" instead of to sign in.
        var result = await _api.GetListAsync<OrganizationUserMembershipResponse>($"/api/organizations/{orgId}/roster", token);
        return result.Map(m => new OrgMembershipItem(m.MembershipId, m.AppUserId, m.Role, m.IsActive, m.DisplayName, m.MemberLevelId, m.MemberLevelName));
    }

    /// <summary>The plan's included role areas (item 156 Phase E). Null degrades to
    /// everything-included — graying is a courtesy; the server enforces regardless.</summary>
    public Task<OrgIncludedAreasItem?> GetOrgIncludedAreasAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<OrgIncludedAreasItem>($"/api/security/organizations/{orgId}/included-areas", token);

    /// <summary>The caller's per-area read verdicts in one group (item 156 Phase D). Null —
    /// e.g. signed out — reads as nothing-visible; the tabs simply do not render.</summary>
    public Task<MyOrgPermissionsItem?> GetMyOrgPermissionsAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<MyOrgPermissionsItem>($"/api/security/organizations/{orgId}/my-permissions", token);

    /// <summary>The caller's own groups, membership rows only — the sidebar's list (item 159).
    /// Never the SuperAdmin sees-all expansion; the token decides, which keeps impersonation
    /// faithful for free.</summary>
    public Task<LoadResult<MyMembershipOrgItem>> GetMyMembershipOrganizationsAsync(CancellationToken token = default)
        => _api.GetListAsync<MyMembershipOrgItem>("/api/security/organizations/my-memberships", token);

    // ── Member-title ladder (item 157) ───────────────────────────────────────

    public Task<LoadResult<OrgMemberLevelItem>> GetMemberLevelsAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrgMemberLevelItem>($"/api/organizations/{orgId}/member-levels", token);

    public Task<OrgMemberLevelItem?> CreateMemberLevelAsync(Guid orgId, string name, int sortOrder, bool isActive, CancellationToken token = default)
        => _api.PostAsync<object, OrgMemberLevelItem>($"/api/organizations/{orgId}/member-levels",
            new { Name = name, SortOrder = sortOrder, IsActive = isActive }, token);

    public Task<OrgMemberLevelItem?> UpdateMemberLevelAsync(Guid orgId, Guid levelId, string name, int sortOrder, bool isActive, CancellationToken token = default)
        => _api.PutAsync<object, OrgMemberLevelItem>($"/api/organizations/{orgId}/member-levels/{levelId}",
            new { Name = name, SortOrder = sortOrder, IsActive = isActive }, token);

    public Task<bool> DeleteMemberLevelAsync(Guid orgId, Guid levelId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/member-levels/{levelId}", token);

    public Task<bool> AssignMemberLevelAsync(Guid orgId, Guid membershipId, Guid? levelId, CancellationToken token = default)
        => _api.PutVoidAsync($"/api/organizations/{orgId}/member-levels/assign/{membershipId}",
            new { MemberLevelId = levelId }, token);

    public async Task<LoadResult<OrgMemberGroupRecord>> GetGroupsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrgMemberGroupRecord>($"/api/organizations/{orgId}/groups", token);
        return result;
    }

    // ── Organization Files ────────────────────────────────────────────────────

    /// <summary>
    /// The group's files, saying so when the list could not be fetched.
    /// </summary>
    /// <remarks>
    /// Sits beside <see cref="GetOrgFilesAsync"/> rather than replacing it: this is the surface
    /// where the refused-reads-as-empty bug was actually found (item 119 — a member with a group
    /// handbook on the server was told the group had no files), so it is the first to get the
    /// honest answer. Other callers move over as they are touched; see item 120 for why this is
    /// not one 136-site rewrite.
    /// </remarks>
    public Task<LoadResult<OrganizationFileRecord>> GetOrgFilesAsync(Guid orgId, CancellationToken token = default)
        => _api.GetListAsync<OrganizationFileRecord>($"/api/organizations/{orgId}/files", token);

    public async Task<LoadResult<OrganizationFileDeleteLogRecord>> GetOrgFileDeleteLogAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationFileDeleteLogRecord>($"/api/organizations/{orgId}/files/delete-log", token);
        return result;
    }

    public Task<OrganizationFileRecord?> UploadOrgFileAsync(Guid orgId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartAsync<OrganizationFileRecord>($"/api/organizations/{orgId}/files", content, token);

    public async Task<OrgFileCopyClientResult?> CopyFileFromUserAsync(Guid orgId, Guid uploadFileId, string? description, bool publishImmediately, CancellationToken token = default)
    {
        // The API returns OrgFileCopyResult which matches OrgFileCopyClientResult shape
        var result = await _api.PostAsync<object, OrgFileCopyClientResult>(
            $"/api/organizations/{orgId}/files/copy-from-user/{uploadFileId}",
            new { Description = description, PublishImmediately = publishImmediately }, token);
        return result;
    }

    public Task<OrganizationFileRecord?> PublishOrgFileAsync(Guid orgId, Guid fileId, bool isPublic, CancellationToken token = default)
        => _api.PutAsync<object, OrganizationFileRecord>(
               $"/api/organizations/{orgId}/files/{fileId}/publish",
               new { IsPublic = isPublic }, token);

    public Task<OrganizationFileRecord?> UpdateOrgFileAsync(Guid orgId, Guid fileId, string? description, int sortOrder, CancellationToken token = default)
        => _api.PutAsync<object, OrganizationFileRecord>(
               $"/api/organizations/{orgId}/files/{fileId}",
               new { Description = description, SortOrder = sortOrder }, token);

    public Task<bool> DeleteOrgFileAsync(Guid orgId, Guid fileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/files/{fileId}", token);

    public Task<OrgMemberGroupRecord?> CreateGroupAsync(Guid orgId, OrgGroupUpsertRequest request, CancellationToken token = default)
        => _api.PostAsync<OrgGroupUpsertRequest, OrgMemberGroupRecord>($"/api/organizations/{orgId}/groups", request, token);

    public Task<OrgMemberGroupRecord?> UpdateGroupAsync(Guid orgId, Guid groupId, OrgGroupUpsertRequest request, CancellationToken token = default)
        => _api.PutAsync<OrgGroupUpsertRequest, OrgMemberGroupRecord>($"/api/organizations/{orgId}/groups/{groupId}", request, token);

    public Task<bool> DeleteGroupAsync(Guid orgId, Guid groupId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/groups/{groupId}", token);

    public async Task<LoadResult<OrgMemberGroupMembershipRecord>> GetGroupMembersAsync(Guid orgId, Guid groupId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrgMemberGroupMembershipRecord>($"/api/organizations/{orgId}/groups/{groupId}/members", token);
        return result;
    }

    public Task<OrgMemberGroupMembershipRecord?> AddGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default)
        => _api.PostAsync<object, OrgMemberGroupMembershipRecord>($"/api/organizations/{orgId}/groups/{groupId}/members",
            new { OrganizationUserMembershipId = membershipId }, token);

    public Task<bool> RemoveGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/groups/{groupId}/members/{membershipId}", token);

    // ── Organization Roles ────────────────────────────────────────────────────────

    public async Task<LoadResult<OrganizationRoleRecord>> GetOrgRolesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationRoleRecord>($"/api/organizations/{orgId}/roles", token);
        return result;
    }

    public Task<OrganizationRoleRecord?> GetOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default)
        => _api.GetAsync<OrganizationRoleRecord>($"/api/organizations/{orgId}/roles/{roleId}", token);

    public Task<OrganizationRoleRecord?> CreateOrgRoleAsync(Guid orgId, CreateOrgRoleRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateOrgRoleRequest, OrganizationRoleRecord>($"/api/organizations/{orgId}/roles", request, token);

    public Task<OrganizationRoleRecord?> UpdateOrgRoleAsync(Guid orgId, Guid roleId, UpdateOrgRoleRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateOrgRoleRequest, OrganizationRoleRecord>($"/api/organizations/{orgId}/roles/{roleId}", request, token);

    public Task<bool> DeleteOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/roles/{roleId}", token);

    public async Task<LoadResult<OrganizationRolePermissionRecord>> GetOrgRolePermissionsAsync(Guid orgId, Guid roleId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationRolePermissionRecord>($"/api/organizations/{orgId}/roles/{roleId}/permissions", token);
        return result;
    }

    public Task<bool> SetOrgRolePermissionsAsync(Guid orgId, Guid roleId, IEnumerable<SetRolePermissionRequest> permissions, CancellationToken token = default)
        => _api.PutVoidAsync($"/api/organizations/{orgId}/roles/{roleId}/permissions", permissions, token);

    public async Task<LoadResult<OrganizationRoleMembershipRecord>> GetOrgRoleMembersAsync(Guid orgId, Guid roleId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationRoleMembershipRecord>($"/api/organizations/{orgId}/roles/{roleId}/members", token);
        return result;
    }

    public Task<OrganizationRoleMembershipRecord?> AddOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid orgUserMembershipId, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationRoleMembershipRecord>($"/api/organizations/{orgId}/roles/{roleId}/members",
            new { OrganizationUserMembershipId = orgUserMembershipId }, token);

    public Task<bool> RemoveOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid membershipId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/roles/{roleId}/members/{membershipId}", token);

    // ── Org address member access ──────────────────────────────────────────────
    public async Task<LoadResult<OrganizationAddressMemberAccessRecord>> GetAddressMemberAccessAsync(Guid orgId, Guid addressId, CancellationToken token = default)
    {
        var result = await _api.GetListAsync<OrganizationAddressMemberAccessRecord>($"/api/organizations/{orgId}/addresses/{addressId}/member-access", token);
        return result;
    }
    public Task<OrganizationAddressMemberAccessRecord?> AddAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid orgUserMembershipId, CancellationToken token = default)
        => _api.PostAsync<AddAddressMemberAccessRequest, OrganizationAddressMemberAccessRecord>($"/api/organizations/{orgId}/addresses/{addressId}/member-access", new(orgUserMembershipId), token);
    public Task<bool> RemoveAddressMemberAccessAsync(Guid orgId, Guid addressId, Guid accessId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/addresses/{addressId}/member-access/{accessId}", token);

    // ── Org settings ──────────────────────────────────────────────────────────
    public Task<OrgSettingsResponse?> GetOrgSettingsAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<OrgSettingsResponse>($"/api/organizations/{orgId}/settings", token);
    public Task<OrgSettingsResponse?> UpdateOrgSettingsAsync(Guid orgId, OrgSettingsRequest request, CancellationToken token = default)
        => _api.PutAsync<OrgSettingsRequest, OrgSettingsResponse>($"/api/organizations/{orgId}/settings", request, token);
}
