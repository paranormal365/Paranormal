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
}
