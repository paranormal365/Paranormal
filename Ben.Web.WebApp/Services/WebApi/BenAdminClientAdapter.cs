using Ben.Data.Common.Enums;
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

    // ── Org Member Groups ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrgMembershipItem>> GetOrganizationMembersAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetOrganizationUsersAsync(orgId, token);
        return result.Select(m => new OrgMembershipItem(m.MembershipId, m.AppUserId, m.Role, m.IsActive)).ToList();
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

    public Task<OrganizationFileRecord?> UploadOrgFileAsync(Guid orgId, MultipartFormDataContent content, CancellationToken token = default)
        => _api.PostMultipartAsync<OrganizationFileRecord>($"/api/organizations/{orgId}/files", content, token);

    public Task<OrganizationFileRecord?> CopyFileFromUserAsync(Guid orgId, Guid uploadFileId, string? description, bool isPublic, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationFileRecord>(
               $"/api/organizations/{orgId}/files/copy-from-user/{uploadFileId}",
               new { Description = description, IsPublic = isPublic }, token);

    public Task<OrganizationFileRecord?> UpdateOrgFileAsync(Guid orgId, Guid fileId, string? description, bool isPublic, int sortOrder, CancellationToken token = default)
        => _api.PutAsync<object, OrganizationFileRecord>(
               $"/api/organizations/{orgId}/files/{fileId}",
               new { Description = description, IsPublic = isPublic, SortOrder = sortOrder }, token);

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

    public async Task<bool> CreateUserAddressTypeAsync(string name, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-address-types", new { Name = name, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty }, token)) is not null;
    public async Task<bool> CreateUserEmailTypeAsync(string name, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-email-types", new { Name = name, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty }, token)) is not null;
    public async Task<bool> CreateUserPhoneTypeAsync(string name, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-phone-types", new { Name = name, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty }, token)) is not null;
    public async Task<bool> CreateUserLinkTypeAsync(string name, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-link-types", new { Name = name, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty }, token)) is not null;
    public async Task<bool> CreateUserNoteTypeAsync(string name, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-note-types", new { Name = name, DateCreated = DateTime.UtcNow, CreatedByAppUserId = Guid.Empty }, token)) is not null;

    // ── User Addresses CRUD ───────────────────────────────────────────────────

    public async Task<bool> CreateUserAddressAsync(Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default)
        => (await _api.PostAsync<object, object>("/api/admin/user-addresses", new {
            AppUserId = userId, UserAddressTypeId = req.UserAddressTypeId,
            StreetAddress1 = req.StreetAddress1, StreetAddress2 = req.StreetAddress2,
            City = req.City, State = req.State, ZipCode = req.ZipCode, Country = req.Country,
            IsPublic = req.IsPublic, SortOrder = req.SortOrder,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = actorId
        }, token)) is not null;

    public async Task<bool> UpdateUserAddressAsync(Guid id, Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default)
        => (await _api.PutAsync<object, object>($"/api/admin/user-addresses/{id}", new {
            Id = id, AppUserId = userId, UserAddressTypeId = req.UserAddressTypeId,
            StreetAddress1 = req.StreetAddress1, StreetAddress2 = req.StreetAddress2,
            City = req.City, State = req.State, ZipCode = req.ZipCode, Country = req.Country,
            IsPublic = req.IsPublic, SortOrder = req.SortOrder,
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
}
