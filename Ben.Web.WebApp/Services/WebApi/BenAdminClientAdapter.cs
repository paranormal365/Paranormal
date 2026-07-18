using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Library.Services;

namespace Ben.Web.WebApp.Services.WebApi;

/// <summary>
/// Implements IBenAdminClient for Ben.Web.Library components by composing
/// IWebApiClient (HTTP) and IWebApiAuthService (token management).
/// </summary>
public sealed class BenAdminClientAdapter : IBenAdminClient
{
    private readonly IWebApiClient _api;
    private readonly IWebApiAuthService _auth;

    public BenAdminClientAdapter(IWebApiClient api, IWebApiAuthService auth)
    {
        _api  = api;
        _auth = auth;
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
}
