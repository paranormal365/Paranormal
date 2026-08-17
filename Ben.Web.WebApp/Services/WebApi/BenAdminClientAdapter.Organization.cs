using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// The Organization half of the adapter — implements <see cref="Ben.Web.Library.Services.IBenOrganizationClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Organizations ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationListItemResponse>> GetOrganizationsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationListItemResponse>>("/api/organizations", token);
        return result ?? [];
    }

    public Task<OrganizationAdminRecord?> GetOrganizationAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<OrganizationAdminRecord>($"/api/organizations/{id}", token);

    public Task<OrganizationAdminRecord?> CreateOrganizationAsync(AdminCreateOrganizationRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateOrganizationRequest, OrganizationAdminRecord>("/api/organizations", request, token);

    public Task<OrganizationAdminRecord?> UpdateOrganizationAsync(Guid id, AdminUpdateOrganizationRequest request, CancellationToken token = default)
        => _api.PutAsync<AdminUpdateOrganizationRequest, OrganizationAdminRecord>($"/api/organizations/{id}", request, token);

    public Task<bool> DeleteOrganizationAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{id}", token);

    // ── Roles ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AdminRoleWithCountResponse>> GetRolesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<AdminRoleWithCountResponse>>("/api/admin/roles", token);
        return result ?? [];
    }

    public Task<AppRoleAdminRecord?> CreateRoleAsync(string roleName, CancellationToken token = default)
        => _api.PostAsync<object, AppRoleAdminRecord>("/api/admin/roles", new { Name = roleName }, token);

    public Task<bool> DeleteRoleAsync(Guid roleId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/roles/{roleId}", token);

    // ── Cross-org visibility (SuperAdmin) ────────────────────────────────────

    public async Task<IReadOnlyList<AdminCaseSummaryRecord>> GetAllCasesAsync(CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<AdminCaseSummaryRecord>>("/api/admin/cases", token) ?? [];

    public async Task<IReadOnlyList<AdminInvestigationSummaryRecord>> GetAllInvestigationsAsync(CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<AdminInvestigationSummaryRecord>>("/api/admin/investigations", token) ?? [];

    // ── Organization Logos ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationLogoRecord>> GetOrgLogosAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationLogoRecord>>($"/api/organizations/{orgId}/logos", token);
        return result ?? [];
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

    public async Task<IReadOnlyList<OrgSearchResult>> SearchOrganizationsAsync(double lat, double lon, int maxResults = 20, CancellationToken token = default)
    {
        var result = await _api.GetAnonymousAsync<IReadOnlyList<OrgSearchResult>>(
            $"/api/public/organizations/search?lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&maxResults={maxResults}", token);
        return result ?? [];
    }

    public Task<OrgBrowsePage?> BrowseOrganizationsAsync(int page = 1, int pageSize = 24, CancellationToken token = default)
        => _api.GetAnonymousAsync<OrgBrowsePage>(
               $"/api/public/organizations/browse?page={page}&pageSize={pageSize}", token);

    // ── Organization Addresses ────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationAddressRecord>> GetOrgAddressesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationAddressRecord>>($"/api/organizations/{orgId}/addresses", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<OrganizationAddressTypeRecord>> GetOrgAddressTypesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationAddressTypeRecord>>("/api/organization-address-types", token);
        return result ?? [];
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

    public string GetFileDownloadUrl(Guid uploadFileId)
        => $"{_webApiBaseUrl}/api/upload-files/{uploadFileId}/download";
    public string GetOrgFileDownloadUrl(Guid orgId, Guid orgFileId)
        => $"{_webApiBaseUrl}/api/organizations/{orgId}/files/{orgFileId}/download";

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

    public async Task<IReadOnlyList<OrgMembershipItem>> GetOrganizationMembersAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetOrganizationUsersAsync(orgId, token);
        return result.Select(m => new OrgMembershipItem(m.MembershipId, m.AppUserId, m.Role, m.IsActive, m.DisplayName)).ToList();
    }

    public async Task<IReadOnlyList<OrgMemberGroupRecord>> GetGroupsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgMemberGroupRecord>>($"/api/organizations/{orgId}/groups", token);
        return result ?? [];
    }

    // ── Organization Files ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationFileRecord>> GetOrgFilesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationFileRecord>>($"/api/organizations/{orgId}/files", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<OrganizationFileDeleteLogRecord>> GetOrgFileDeleteLogAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationFileDeleteLogRecord>>($"/api/organizations/{orgId}/files/delete-log", token);
        return result ?? [];
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

    public async Task<IReadOnlyList<OrgMemberGroupMembershipRecord>> GetGroupMembersAsync(Guid orgId, Guid groupId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgMemberGroupMembershipRecord>>($"/api/organizations/{orgId}/groups/{groupId}/members", token);
        return result ?? [];
    }

    public Task<OrgMemberGroupMembershipRecord?> AddGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default)
        => _api.PostAsync<object, OrgMemberGroupMembershipRecord>($"/api/organizations/{orgId}/groups/{groupId}/members",
            new { OrganizationUserMembershipId = membershipId }, token);

    public Task<bool> RemoveGroupMemberAsync(Guid orgId, Guid groupId, Guid membershipId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/groups/{groupId}/members/{membershipId}", token);

    // ── Organization Roles ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationRoleRecord>> GetOrgRolesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationRoleRecord>>($"/api/organizations/{orgId}/roles", token);
        return result ?? [];
    }

    public Task<OrganizationRoleRecord?> GetOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default)
        => _api.GetAsync<OrganizationRoleRecord>($"/api/organizations/{orgId}/roles/{roleId}", token);

    public Task<OrganizationRoleRecord?> CreateOrgRoleAsync(Guid orgId, CreateOrgRoleRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateOrgRoleRequest, OrganizationRoleRecord>($"/api/organizations/{orgId}/roles", request, token);

    public Task<OrganizationRoleRecord?> UpdateOrgRoleAsync(Guid orgId, Guid roleId, UpdateOrgRoleRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateOrgRoleRequest, OrganizationRoleRecord>($"/api/organizations/{orgId}/roles/{roleId}", request, token);

    public Task<bool> DeleteOrgRoleAsync(Guid orgId, Guid roleId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/roles/{roleId}", token);

    public async Task<IReadOnlyList<OrganizationRolePermissionRecord>> GetOrgRolePermissionsAsync(Guid orgId, Guid roleId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationRolePermissionRecord>>($"/api/organizations/{orgId}/roles/{roleId}/permissions", token);
        return result ?? [];
    }

    public Task<bool> SetOrgRolePermissionsAsync(Guid orgId, Guid roleId, IEnumerable<SetRolePermissionRequest> permissions, CancellationToken token = default)
        => _api.PutVoidAsync($"/api/organizations/{orgId}/roles/{roleId}/permissions", permissions, token);

    public async Task<IReadOnlyList<OrganizationRoleMembershipRecord>> GetOrgRoleMembersAsync(Guid orgId, Guid roleId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationRoleMembershipRecord>>($"/api/organizations/{orgId}/roles/{roleId}/members", token);
        return result ?? [];
    }

    public Task<OrganizationRoleMembershipRecord?> AddOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid orgUserMembershipId, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationRoleMembershipRecord>($"/api/organizations/{orgId}/roles/{roleId}/members",
            new { OrganizationUserMembershipId = orgUserMembershipId }, token);

    public Task<bool> RemoveOrgRoleMemberAsync(Guid orgId, Guid roleId, Guid membershipId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/roles/{roleId}/members/{membershipId}", token);

    // ── Org address member access ──────────────────────────────────────────────
    public async Task<IReadOnlyList<OrganizationAddressMemberAccessRecord>> GetAddressMemberAccessAsync(Guid orgId, Guid addressId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationAddressMemberAccessRecord>>($"/api/organizations/{orgId}/addresses/{addressId}/member-access", token);
        return result ?? [];
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
