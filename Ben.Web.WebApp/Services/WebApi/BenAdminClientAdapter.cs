using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// Implements IBenAdminClient for Ben.Web.Library components by composing
/// IWebApiClient (HTTP) and IWebApiAuthService (token management).
/// </summary>
public sealed class BenAdminClientAdapter : IBenAdminClient
{
    private readonly IWebApiClient _api;
    private readonly IWebApiAuthService _auth;
    private readonly string _webApiBaseUrl;

    public BenAdminClientAdapter(IWebApiClient api, IWebApiAuthService auth, IOptions<WebApiOptions> options)
    {
        _api           = api;
        _auth          = auth;
        _webApiBaseUrl = options.Value.BaseUrl.TrimEnd('/');
    }

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

    // ── Audit Log ─────────────────────────────────────────────────────────────

    public async Task<AuditLogPagedResponse?> GetAuditLogsAsync(
        int page = 1, int pageSize = 50, string? entityType = null, int? action = null,
        Guid? userId = null, DateTime? dateFrom = null, DateTime? dateTo = null,
        CancellationToken token = default)
    {
        var qs = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(entityType)) qs += $"&entityType={Uri.EscapeDataString(entityType)}";
        if (action.HasValue)    qs += $"&action={action.Value}";
        if (userId.HasValue)    qs += $"&userId={userId.Value}";
        if (dateFrom.HasValue)  qs += $"&dateFrom={Uri.EscapeDataString(dateFrom.Value.ToString("o"))}";
        if (dateTo.HasValue)    qs += $"&dateTo={Uri.EscapeDataString(dateTo.Value.ToString("o"))}";
        return await _api.GetAsync<AuditLogPagedResponse>($"/api/admin/audit-logs{qs}", token);
    }

    public async Task<IReadOnlyList<string>> GetAuditLogEntityTypesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<string>>("/api/admin/audit-logs/entity-types", token);
        return result ?? [];
    }

    public Task<bool> SendAuditLogMessageAsync(SendAuditLogMessageRequest request, CancellationToken token = default)
        => _api.PostAsync<SendAuditLogMessageRequest, bool>("/api/admin/audit-logs/send-message", request, token);

    // ── Users ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AppUserRecord>> GetAllUsersAsync(CancellationToken token = default)
        => await _api.GetUsersAsync(token);

    public Task<AppUserDetailAdminRecord?> GetUserDetailAsync(Guid userId, CancellationToken token = default)
        => _api.GetAsync<AppUserDetailAdminRecord>($"/api/admin/app-users/{userId}/detail", token);

    public Task<AppUserAdminRecord?> CreateUserAsync(AdminCreateUserRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateUserRequest, AppUserAdminRecord>("/api/admin/app-users", request, token);

    public Task<AppUserAdminRecord?> UpdateUserProfileAsync(Guid userId, AdminUpdateUserProfileRequest request, CancellationToken token = default)
        => _api.PutAsync<AdminUpdateUserProfileRequest, AppUserAdminRecord>($"/api/admin/app-users/{userId}/profile", request, token);

    public Task<bool> ImpersonateUserAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default)
        => _auth.ImpersonateAsync(targetUserId, targetUserEmail, token);

    public void StopImpersonating()
        => _auth.StopImpersonating();

    // ── File Types ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AdminFileTypeWithExtensionsResponse>> GetFileTypesWithExtensionsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<AdminFileTypeWithExtensionsResponse>>("/api/admin/upload-file-types/with-extensions", token);
        return result ?? [];
    }

    public Task<UploadFileTypeRecord?> CreateFileTypeAsync(AdminCreateFileTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateFileTypeRequest, UploadFileTypeRecord>("/api/admin/upload-file-types", request, token);

    public Task<UploadFileTypeRecord?> UpdateFileTypeAsync(Guid id, AdminUpdateFileTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<AdminUpdateFileTypeRequest, UploadFileTypeRecord>($"/api/admin/upload-file-types/{id}", request, token);

    public Task<bool> DeleteFileTypeAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/upload-file-types/{id}", token);

    // ── File Type Extensions ──────────────────────────────────────────────────

    public Task<UploadFileTypeExtensionRecord?> CreateFileTypeExtensionAsync(AdminCreateFileTypeExtensionRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateFileTypeExtensionRequest, UploadFileTypeExtensionRecord>("/api/admin/upload-file-type-extensions", request, token);

    public Task<UploadFileTypeExtensionRecord?> UpdateFileTypeExtensionAsync(Guid id, string pattern, CancellationToken token = default)
        => _api.PutAsync<object, UploadFileTypeExtensionRecord>($"/api/admin/upload-file-type-extensions/{id}", new { Pattern = pattern }, token);

    public Task<bool> DeleteFileTypeExtensionAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/upload-file-type-extensions/{id}", token);

    // ── Generic Lookup Types ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<LookupTypeAdminRecord>> GetLookupTypesAsync(string route, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<LookupTypeAdminRecord>>($"/{route}", token);
        return result ?? [];
    }

    public Task<LookupTypeAdminRecord?> CreateLookupTypeAsync(string route, LookupTypeUpsertRequest request, CancellationToken token = default)
        => _api.PostAsync<LookupTypeUpsertRequest, LookupTypeAdminRecord>($"/{route}", request, token);

    public Task<LookupTypeAdminRecord?> UpdateLookupTypeAsync(string route, Guid id, LookupTypeUpsertRequest request, CancellationToken token = default)
        => _api.PutAsync<LookupTypeUpsertRequest, LookupTypeAdminRecord>($"/{route}/{id}", request, token);

    public Task<bool> DeleteLookupTypeAsync(string route, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/{route}/{id}", token);

    // ── CMS Pages ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CmsPageListItem>> GetCmsPagesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CmsPageListItem>>($"/api/organizations/{orgId}/pages", token);
        return result ?? [];
    }

    public Task<CmsPageDetail?> GetCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.GetAsync<CmsPageDetail>($"/api/organizations/{orgId}/pages/{pageId}", token);

    public Task<CmsPageDetail?> CreateCmsPageAsync(Guid orgId, CmsCreatePageRequest request, CancellationToken token = default)
        => _api.PostAsync<CmsCreatePageRequest, CmsPageDetail>($"/api/organizations/{orgId}/pages", request, token);

    public Task<CmsPageDetail?> UpdateCmsPageAsync(Guid orgId, Guid pageId, CmsUpdatePageRequest request, CancellationToken token = default)
        => _api.PutAsync<CmsUpdatePageRequest, CmsPageDetail>($"/api/organizations/{orgId}/pages/{pageId}", request, token);

    public Task<bool> DeleteCmsPageAsync(Guid orgId, Guid pageId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/pages/{pageId}", token);

    // ── CMS Sections ──────────────────────────────────────────────────────────

    public Task<CmsSectionRecord?> CreateCmsSectionAsync(Guid orgId, Guid pageId, CmsCreateSectionRequest request, CancellationToken token = default)
        => _api.PostAsync<CmsCreateSectionRequest, CmsSectionRecord>($"/api/organizations/{orgId}/pages/{pageId}/sections", request, token);

    public Task<CmsSectionRecord?> UpdateCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CmsUpdateSectionRequest request, CancellationToken token = default)
        => _api.PutAsync<CmsUpdateSectionRequest, CmsSectionRecord>($"/api/organizations/{orgId}/pages/{pageId}/sections/{sectionId}", request, token);

    public Task<bool> ReorderCmsSectionsAsync(Guid orgId, Guid pageId, IList<Guid> orderedIds, CancellationToken token = default)
        => _api.PutVoidAsync($"/api/organizations/{orgId}/pages/{pageId}/sections/reorder",
               new { OrderedSectionIds = orderedIds }, token);

    public Task<bool> DeleteCmsSectionAsync(Guid orgId, Guid pageId, Guid sectionId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/pages/{pageId}/sections/{sectionId}", token);

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
        => _api.GetAsync<GeocodingPreviewResponse>($"/api/geocode/search?q={Uri.EscapeDataString(query)}", token);

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

    public string GetFileDownloadUrl(Guid uploadFileId)
        => $"{_webApiBaseUrl}/api/upload-files/{uploadFileId}/download";

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

    // ── Membership Requests ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationMembershipRequestRecord>> GetMembershipRequestsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationMembershipRequestRecord>>($"/api/organizations/{orgId}/membership-requests", token);
        return result ?? [];
    }

    public Task<OrganizationMembershipRequestRecord?> GetMyMembershipRequestAsync(Guid orgId, CancellationToken token = default)
        => _api.GetAsync<OrganizationMembershipRequestRecord>($"/api/organizations/{orgId}/membership-requests/my", token);

    public Task<OrganizationMembershipRequestRecord?> ApplyForMembershipAsync(Guid orgId, string? message, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests", new { Message = message }, token);

    public Task<OrganizationMembershipRequestRecord?> RespondToMembershipRequestAsync(
        Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote, CancellationToken token = default)
        => _api.PutAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/respond",
               new { Status = status, ResponseNote = responseNote }, token);

    public Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/membership-requests/{requestId}", token);

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

    // ── CMS Page Permissions ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<CmsPagePermissionRecord>> GetPagePermissionsAsync(Guid orgId, Guid pageId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CmsPagePermissionRecord>>($"/api/organizations/{orgId}/pages/{pageId}/permissions", token);
        return result ?? [];
    }

    public Task<CmsPagePermissionRecord?> CreatePagePermissionAsync(Guid orgId, Guid pageId, PagePermissionCreateRequest request, CancellationToken token = default)
        => _api.PostAsync<PagePermissionCreateRequest, CmsPagePermissionRecord>($"/api/organizations/{orgId}/pages/{pageId}/permissions", request, token);

    public Task<CmsPagePermissionRecord?> UpdatePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CmsPageAction actions, CancellationToken token = default)
        => _api.PutAsync<object, CmsPagePermissionRecord>($"/api/organizations/{orgId}/pages/{pageId}/permissions/{permId}",
            new { Actions = actions }, token);

    public Task<bool> DeletePagePermissionAsync(Guid orgId, Guid pageId, Guid permId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/pages/{pageId}/permissions/{permId}", token);

    // ── User sub-entity type lists ────────────────────────────────────────────

    public async Task<IReadOnlyList<UserAddressTypeRecord>> GetUserAddressTypesAsync(CancellationToken token = default)
        => (await _api.GetAsync<List<UserAddressTypeRecord>>("/api/user-address-types", token)) ?? [];
    public async Task<IReadOnlyList<UserEmailTypeRecord>> GetUserEmailTypesAsync(CancellationToken token = default)
        => (await _api.GetAsync<List<UserEmailTypeRecord>>("/api/user-email-types", token)) ?? [];
    public async Task<IReadOnlyList<UserPhoneTypeRecord>> GetUserPhoneTypesAsync(CancellationToken token = default)
        => (await _api.GetAsync<List<UserPhoneTypeRecord>>("/api/user-phone-types", token)) ?? [];
    public async Task<IReadOnlyList<UserLinkTypeRecord>> GetUserLinkTypesAsync(CancellationToken token = default)
        => (await _api.GetAsync<List<UserLinkTypeRecord>>("/api/user-link-types", token)) ?? [];
    public async Task<IReadOnlyList<UserNoteTypeRecord>> GetUserNoteTypesAsync(CancellationToken token = default)
        => (await _api.GetAsync<List<UserNoteTypeRecord>>("/api/user-note-types", token)) ?? [];

    // ── User sub-entity type creation ─────────────────────────────────────────

    public async Task<bool> CreateUserAddressTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-address-types", new { Name = name, Description = description, IsActive = isActive, IsPublic = isPublic, SortOrder = sortOrder, IconClass = iconClass, ColorClass = colorClass }, token)) is not null;
    public async Task<bool> CreateUserEmailTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-email-types", new { Name = name, Description = description, IsActive = isActive, IsPublic = isPublic, SortOrder = sortOrder, IconClass = iconClass, ColorClass = colorClass }, token)) is not null;
    public async Task<bool> CreateUserPhoneTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-phone-types", new { Name = name, Description = description, IsActive = isActive, IsPublic = isPublic, SortOrder = sortOrder, IconClass = iconClass, ColorClass = colorClass }, token)) is not null;
    public async Task<bool> CreateUserLinkTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-link-types", new { Name = name, Description = description, IsActive = isActive, IsPublic = isPublic, SortOrder = sortOrder, IconClass = iconClass, ColorClass = colorClass }, token)) is not null;
    public async Task<bool> CreateUserNoteTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-note-types", new { Name = name, Description = description, IsActive = isActive, IsPublic = isPublic, SortOrder = sortOrder, IconClass = iconClass, ColorClass = colorClass }, token)) is not null;

    // ── User Addresses CRUD ───────────────────────────────────────────────────

    public async Task<bool> CreateUserAddressAsync(Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-addresses", new {
            AppUserId = userId, UserAddressTypeId = req.UserAddressTypeId,
            StreetAddress1 = req.StreetAddress1, StreetAddress2 = req.StreetAddress2,
            City = req.City, State = req.State, ZipCode = req.ZipCode, Country = req.Country,
            IsPublic = req.IsPublic, SortOrder = req.SortOrder,
            Latitude = req.Latitude, Longitude = req.Longitude,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actorId
        }, token)) is not null;

    public async Task<bool> UpdateUserAddressAsync(Guid id, Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default)
        => (await _api.PutAsync<object, object>($"/api/admin/user-addresses/{id}", new {
            Id = id, AppUserId = userId, UserAddressTypeId = req.UserAddressTypeId,
            StreetAddress1 = req.StreetAddress1, StreetAddress2 = req.StreetAddress2,
            City = req.City, State = req.State, ZipCode = req.ZipCode, Country = req.Country,
            IsPublic = req.IsPublic, SortOrder = req.SortOrder,
            Latitude = req.Latitude, Longitude = req.Longitude,
            DateCreated = DateTime.UtcNow, UpdatedByAppUserId = actorId
        }, token)) is not null;

    public Task<bool> DeleteUserAddressAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/user-addresses/{id}", token);

    // ── User Emails CRUD ──────────────────────────────────────────────────────

    public async Task<bool> CreateUserEmailAsync(Guid userId, Guid actorId, UserEmailUpsertRequest req, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-emails", new {
            AppUserId = userId, UserEmailTypeId = req.UserEmailTypeId,
            EmailAddress = req.EmailAddress, IsPrimary = req.IsPrimary, IsPublic = req.IsPublic,
            IsHidden = false, IsValidated = false, ValidationToken = string.Empty, SortOrder = req.SortOrder,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actorId
        }, token)) is not null;

    public async Task<bool> UpdateUserEmailAsync(Guid id, Guid userId, Guid actorId, UserEmailUpsertRequest req, CancellationToken token = default)
        => (await _api.PutAsync<object, object>($"/api/admin/user-emails/{id}", new {
            Id = id, AppUserId = userId, UserEmailTypeId = req.UserEmailTypeId,
            EmailAddress = req.EmailAddress, IsPrimary = req.IsPrimary, IsPublic = req.IsPublic,
            IsHidden = false, IsValidated = false, ValidationToken = string.Empty, SortOrder = req.SortOrder,
            DateCreated = DateTime.UtcNow, UpdatedByAppUserId = actorId
        }, token)) is not null;

    public Task<bool> DeleteUserEmailAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/user-emails/{id}", token);

    // ── User Phones CRUD ──────────────────────────────────────────────────────

    public async Task<bool> CreateUserPhoneAsync(Guid userId, Guid actorId, UserPhoneUpsertRequest req, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-phones", new {
            AppUserId = userId, UserPhoneTypeId = req.UserPhoneTypeId,
            PhoneNumber = req.PhoneNumber, PhoneCountry = req.PhoneCountry,
            IsPrimary = req.IsPrimary, IsCellular = req.IsCellular, IsPublic = req.IsPublic,
            IsValidated = false, ValidationToken = string.Empty,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actorId
        }, token)) is not null;

    public async Task<bool> UpdateUserPhoneAsync(Guid id, Guid userId, Guid actorId, UserPhoneUpsertRequest req, CancellationToken token = default)
        => (await _api.PutAsync<object, object>($"/api/admin/user-phones/{id}", new {
            Id = id, AppUserId = userId, UserPhoneTypeId = req.UserPhoneTypeId,
            PhoneNumber = req.PhoneNumber, PhoneCountry = req.PhoneCountry,
            IsPrimary = req.IsPrimary, IsCellular = req.IsCellular, IsPublic = req.IsPublic,
            IsValidated = false, ValidationToken = string.Empty,
            DateCreated = DateTime.UtcNow, UpdatedByAppUserId = actorId
        }, token)) is not null;

    public Task<bool> DeleteUserPhoneAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/user-phones/{id}", token);

    // ── User Links CRUD ───────────────────────────────────────────────────────

    public async Task<bool> CreateUserLinkAsync(Guid userId, Guid actorId, UserLinkUpsertRequest req, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-links", new {
            AppUserId = userId, UserLinkTypeId = req.UserLinkTypeId,
            LinkUrl = req.LinkUrl, DisplayText = req.DisplayText,
            IsPublic = req.IsPublic, IsActive = req.IsActive,
            IsVerifiedApproved = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actorId
        }, token)) is not null;

    public async Task<bool> UpdateUserLinkAsync(Guid id, Guid userId, Guid actorId, UserLinkUpsertRequest req, CancellationToken token = default)
        => (await _api.PutAsync<object, object>($"/api/admin/user-links/{id}", new {
            Id = id, AppUserId = userId, UserLinkTypeId = req.UserLinkTypeId,
            LinkUrl = req.LinkUrl, DisplayText = req.DisplayText,
            IsPublic = req.IsPublic, IsActive = req.IsActive,
            IsVerifiedApproved = false,
            DateCreated = DateTime.UtcNow, UpdatedByAppUserId = actorId
        }, token)) is not null;

    public Task<bool> DeleteUserLinkAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/user-links/{id}", token);

    // ── User Notes CRUD ───────────────────────────────────────────────────────

    public async Task<bool> CreateUserNoteAsync(Guid userId, Guid actorId, UserNoteUpsertRequest req, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-notes", new {
            CreatedByAppUserId = actorId, UserNoteTypeId = req.UserNoteTypeId,
            NoteSubject = req.NoteSubject, NoteBody = req.NoteBody, IsPublic = req.IsPublic,
            DateCreated = DateTime.UtcNow
        }, token)) is not null;

    public async Task<bool> UpdateUserNoteAsync(Guid id, Guid userId, Guid actorId, UserNoteUpsertRequest req, CancellationToken token = default)
        => (await _api.PutAsync<object, object>($"/api/admin/user-notes/{id}", new {
            Id = id, CreatedByAppUserId = userId, UserNoteTypeId = req.UserNoteTypeId,
            NoteSubject = req.NoteSubject, NoteBody = req.NoteBody, IsPublic = req.IsPublic,
            DateCreated = DateTime.UtcNow, UpdatedByAppUserId = actorId
        }, token)) is not null;

    public Task<bool> DeleteUserNoteAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/user-notes/{id}", token);

    // ── File admin delete ─────────────────────────────────────────────────────

    public Task<bool> DeleteUploadFileAdminAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/upload-files/{id}", token);

    // ── Client Requests ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ClientRequestRecord>> GetMyClientRequestsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ClientRequestRecord>>("/api/client-requests/my", token);
        return result ?? [];
    }

    public Task<ClientRequestRecord?> GetClientRequestAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<ClientRequestRecord>($"/api/client-requests/{id}", token);

    public async Task<IReadOnlyList<ClientRequestOrganizationRecord>> GetClientRequestOrgsAsync(Guid id, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ClientRequestOrganizationRecord>>($"/api/client-requests/{id}/organizations", token);
        return result ?? [];
    }

    public Task<ClientRequestRecord?> CreateClientRequestAsync(UpsertClientRequestRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertClientRequestRequest, ClientRequestRecord>("/api/client-requests", request, token);

    public Task<ClientRequestRecord?> UpdateClientRequestAsync(Guid id, UpsertClientRequestRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertClientRequestRequest, ClientRequestRecord>($"/api/client-requests/{id}", request, token);

    public Task<ClientRequestRecord?> SubmitClientRequestAsync(Guid id, IList<Guid> organizationIds, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>($"/api/client-requests/{id}/submit",
               new { OrganizationIds = organizationIds }, token);

    public Task<ClientRequestRecord?> WithdrawClientRequestAsync(Guid id, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>($"/api/client-requests/{id}/withdraw", new { }, token);

    // ── Experience Taxonomy ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<ExperienceCategoryWithTypesResponse>> GetExperienceTaxonomyAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ExperienceCategoryWithTypesResponse>>("/api/experience-categories/with-types", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<ExperienceCategoryRecord>> GetAllExperienceCategoriesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ExperienceCategoryRecord>>("/api/admin/experience-categories", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<ExperienceTypeRecord>> GetAllExperienceTypesAsync(Guid categoryId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ExperienceTypeRecord>>($"/api/admin/experience-categories/{categoryId}/types", token);
        return result ?? [];
    }

    public Task<ExperienceCategoryRecord?> CreateExperienceCategoryAsync(UpsertExperienceCategoryRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>("/api/admin/experience-categories", request, token);

    public Task<ExperienceCategoryRecord?> UpdateExperienceCategoryAsync(Guid id, UpsertExperienceCategoryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertExperienceCategoryRequest, ExperienceCategoryRecord>($"/api/admin/experience-categories/{id}", request, token);

    public Task<bool> DeleteExperienceCategoryAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/experience-categories/{id}", token);

    public Task<ExperienceCategoryRecord?> ApproveExperienceCategoryAsync(Guid id, CancellationToken token = default)
        => _api.PutAsync<object, ExperienceCategoryRecord>($"/api/admin/experience-categories/{id}/approve", new { }, token);

    public Task<ExperienceTypeRecord?> CreateExperienceTypeAsync(Guid categoryId, UpsertExperienceTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertExperienceTypeRequest, ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types", request, token);

    public Task<ExperienceTypeRecord?> UpdateExperienceTypeAsync(Guid categoryId, Guid id, UpsertExperienceTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertExperienceTypeRequest, ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types/{id}", request, token);

    public Task<bool> DeleteExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/experience-categories/{categoryId}/types/{id}", token);

    public Task<ExperienceTypeRecord?> ApproveExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default)
        => _api.PutAsync<object, ExperienceTypeRecord>($"/api/admin/experience-categories/{categoryId}/types/{id}/approve", new { }, token);

    // ── CMS File Library ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<UploadFileRecord>> GetOrgSharedFilesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetOrgSharedFilesAsync(orgId, token);
        return result ?? [];
    }

    public async Task<(byte[] Data, string ContentType)?> GetFileDataAsync(Guid fileId, CancellationToken token = default)
    {
        var result = await _api.DownloadFileAsync(fileId, token);
        if (result is null) return null;
        return (result.Value.Data, result.Value.ContentType);
    }

    public async Task<IReadOnlyList<UploadFileTypeRecord>> GetPublicFileTypesAsync(CancellationToken token = default)
        => await _api.GetUploadFileTypesAsync(token);

    public async Task<UploadFileRecord?> UploadImageAsync(
        Guid fileTypeId, Guid userId, string fileName, string contentType, byte[] data,
        CancellationToken token = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()), "uploadFileTypeId");
        form.Add(new StringContent(userId.ToString()), "appUserId");
        form.Add(new StringContent(""), "description");
        form.Add(new StringContent("true"), "isPublic");
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return await _api.UploadFileAsync(form, token);
    }

    public async Task<UploadFileRecord?> UploadUserFileAsync(
        Guid fileTypeId, Guid userId, string fileName, string contentType, byte[] data,
        string? description = null, bool isPublic = false, CancellationToken token = default)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(fileTypeId.ToString()),             "uploadFileTypeId");
        form.Add(new StringContent(userId.ToString()),                 "appUserId");
        form.Add(new StringContent(description ?? string.Empty),       "description");
        form.Add(new StringContent(isPublic ? "true" : "false"),       "isPublic");
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        return await _api.UploadFileAsync(form, token);
    }

    // ── Audio Config ─────────────────────────────────────────────────────

    public Task<UploadFileAudioConfigRecord?> GetAudioConfigAsync(Guid fileId, CancellationToken token = default)
        => _api.GetAudioConfigAsync(fileId, token);

    public Task<UploadFileAudioConfigRecord?> UpsertAudioConfigAsync(Guid fileId, UpsertAudioConfigRequest request, CancellationToken token = default)
        => _api.UpsertAudioConfigAsync(fileId, request, token);

    public Task<bool> DeleteAudioConfigAsync(Guid fileId, CancellationToken token = default)
        => _api.DeleteAudioConfigAsync(fileId, token);

    // ── Region Notes ──────────────────────────────────────────────────────────

    public Task<IReadOnlyList<UploadFileRegionNoteRecord>> GetRegionNotesAsync(Guid fileId, CancellationToken token = default)
        => _api.GetRegionNotesAsync(fileId, token);

    public Task<UploadFileRegionNoteRecord?> CreateRegionNoteAsync(Guid fileId, CreateRegionNoteRequest request, CancellationToken token = default)
        => _api.CreateRegionNoteAsync(fileId, request, token);

    public Task<UploadFileRegionNoteRecord?> UpdateRegionNoteAsync(Guid fileId, Guid noteId, UpdateRegionNoteRequest request, CancellationToken token = default)
        => _api.UpdateRegionNoteAsync(fileId, noteId, request, token);

    public Task<bool> DeleteRegionNoteAsync(Guid fileId, Guid noteId, CancellationToken token = default)
        => _api.DeleteRegionNoteAsync(fileId, noteId, token);

    // ── Audio Clip ────────────────────────────────────────────────────────────

    public Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default)
        => _api.ClipAudioAsync(fileId, request, token);

    public Task<IReadOnlyList<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetChildClipsAsync(fileId, token);

    public Task<(byte[] Data, string ContentType)?> GetClipPreviewAsync(Guid fileId, double start, double end, CancellationToken token = default)
        => _api.GetClipPreviewAsync(fileId, start, end, token);

    // ── Votes ────────────────────────────────────────────────

    public Task<UploadFileVoteSummary?> GetVoteSummaryAsync(Guid fileId, CancellationToken token = default)
        => _api.GetVoteSummaryAsync(fileId, token);

    public Task<UploadFileVoteRecord?> UpsertMyVoteAsync(Guid fileId, int score, CancellationToken token = default)
        => _api.UpsertMyVoteAsync(fileId, score, token);

    public Task<bool> RemoveMyVoteAsync(Guid fileId, CancellationToken token = default)
        => _api.RemoveMyVoteAsync(fileId, token);

    // ── Directions ────────────────────────────────────────────────────────────
    public Task<DirectionsResult?> GetDirectionsAsync(double fromLat, double fromLon, double toLat, double toLon, CancellationToken token = default)
    {
        var fLat = fromLat.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var fLon = fromLon.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var tLat = toLat.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        var tLon = toLon.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        return _api.GetAsync<DirectionsResult>($"/api/directions?fromLat={fLat}&fromLon={fLon}&toLat={tLat}&toLon={tLon}", token);
    }

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
