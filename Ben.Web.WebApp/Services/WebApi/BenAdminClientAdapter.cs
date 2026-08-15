using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
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

    // ── Cross-org visibility (SuperAdmin) ────────────────────────────────────

    public async Task<IReadOnlyList<AdminCaseSummaryRecord>> GetAllCasesAsync(CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<AdminCaseSummaryRecord>>("/api/admin/cases", token) ?? [];

    public async Task<IReadOnlyList<AdminInvestigationSummaryRecord>> GetAllInvestigationsAsync(CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<AdminInvestigationSummaryRecord>>("/api/admin/investigations", token) ?? [];

    // ── Universal media library sharing ──────────────────────────────────────

    public async Task<IReadOnlyList<UploadFileShareRecord>> GetSharesV2Async(Guid fileId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<UploadFileShareRecord>>($"/api/upload-files/{fileId}/shares-v2", token) ?? [];

    public Task<UploadFileShareRecord?> CreateShareAsync(Guid fileId, CreateShareRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateShareRequest, UploadFileShareRecord>($"/api/upload-files/{fileId}/shares-v2", request, token);

    public Task<bool> RemoveShareV2Async(Guid shareId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/upload-file-shares-v2/{shareId}", token);

    public async Task<IReadOnlyList<UploadFileRecord>> GetMediaLibraryFilesAsync(string[]? contentTypePrefixes = null, CancellationToken token = default)
    {
        var url = "/api/media-library/files";
        if (contentTypePrefixes is { Length: > 0 })
            url += $"?contentTypePrefixes={Uri.EscapeDataString(string.Join(',', contentTypePrefixes))}";
        return await _api.GetAsync<IReadOnlyList<UploadFileRecord>>(url, token) ?? [];
    }

    // ── Notifications ─────────────────────────────────────────────────────────

    public Task<NotificationSummaryResponse?> GetNotificationSummaryAsync(CancellationToken token = default)
        => _api.GetAsync<NotificationSummaryResponse>("/api/me/notification-summary", token);

    public async Task<List<MyMessageRecord>> GetMyMessagesAsync(bool unreadOnly = false, CancellationToken token = default)
        => await _api.GetAsync<List<MyMessageRecord>>(
               $"/api/me/messages?unreadOnly={(unreadOnly ? "true" : "false")}", token) ?? [];

    public Task<bool> MarkMyMessageReadAsync(Guid id, CancellationToken token = default)
        => _api.PutVoidAsync<object?>($"/api/me/messages/{id}/read", null, token);

    // ── My profile (Area 4 / U1) ─────────────────────────────────────────────

    public Task<MyProfileRecord?> GetMyProfileAsync(CancellationToken token = default)
        => _api.GetAsync<MyProfileRecord>("/api/me/profile", token);

    public Task<MyProfileRecord?> UpdateMyProfileAsync(
        UpdateMyProfileRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateMyProfileRequest, MyProfileRecord>("/api/me/profile", request, token);

    public async Task<List<AppUserPhotoRecord>> GetMyPhotosAsync(CancellationToken token = default)
        => await _api.GetAsync<List<AppUserPhotoRecord>>("/api/me/photos", token) ?? [];

    public Task<AppUserPhotoRecord?> SetMyPhotoAsync(
        SetMyPhotoRequest request, CancellationToken token = default)
        => _api.PostAsync<SetMyPhotoRequest, AppUserPhotoRecord>("/api/me/photos", request, token);

    public Task<bool> DeleteMyPhotoAsync(Guid photoId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/me/photos/{photoId}", token);

    public Task<Guid?> GetProfilePhotoFileTypeIdAsync(CancellationToken token = default)
        => _api.GetAsync<Guid?>("/api/me/photos/file-type", token);

    public async Task<(byte[] Data, string ContentType)?> GetUserAvatarAsync(
        Guid userId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/users/{userId}/avatar", "avatar", token);
        // A 204 (no photo this viewer may see) comes back as empty rather than as an error.
        return result is { } r && r.Data.Length > 0 ? (r.Data, r.ContentType) : null;
    }

    public async Task<int> MarkAllMyMessagesReadAsync(CancellationToken token = default)
        => await _api.PutAsync<object?, int>("/api/me/messages/read-all", null, token);

    public async Task<List<PendingPermissionRequestRecord>> GetPendingPermissionRequestsForMeAsync(CancellationToken token = default)
        => await _api.GetAsync<List<PendingPermissionRequestRecord>>("/api/me/permission-requests/pending", token) ?? [];

    // ── My contact info ────────────────────────────────────────────────────────

    public async Task<List<MyEmailRecord>> GetMyEmailsAsync(CancellationToken token = default)
        => await _api.GetAsync<List<MyEmailRecord>>("/api/me/emails", token) ?? [];

    public Task<MyEmailRecord?> CreateMyEmailAsync(UpsertMyEmailRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertMyEmailRequest, MyEmailRecord>("/api/me/emails", request, token);

    public Task<MyEmailRecord?> UpdateMyEmailAsync(Guid id, UpsertMyEmailRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertMyEmailRequest, MyEmailRecord>($"/api/me/emails/{id}", request, token);

    public Task<bool> DeleteMyEmailAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/me/emails/{id}", token);

    public Task<SendValidationResponse?> SendMyEmailValidationAsync(Guid id, CancellationToken token = default)
        => _api.PostAsync<object?, SendValidationResponse>($"/api/me/emails/{id}/send-validation", null, token);

    public async Task<List<MyPhoneRecord>> GetMyPhonesAsync(CancellationToken token = default)
        => await _api.GetAsync<List<MyPhoneRecord>>("/api/me/phones", token) ?? [];

    public Task<MyPhoneRecord?> CreateMyPhoneAsync(UpsertMyPhoneRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertMyPhoneRequest, MyPhoneRecord>("/api/me/phones", request, token);

    public Task<MyPhoneRecord?> UpdateMyPhoneAsync(Guid id, UpsertMyPhoneRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertMyPhoneRequest, MyPhoneRecord>($"/api/me/phones/{id}", request, token);

    public Task<bool> DeleteMyPhoneAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/me/phones/{id}", token);

    public async Task<List<MyAddressRecord>> GetMyAddressesAsync(CancellationToken token = default)
        => await _api.GetAsync<List<MyAddressRecord>>("/api/me/addresses", token) ?? [];

    public Task<MyAddressRecord?> CreateMyAddressAsync(UpsertMyAddressRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertMyAddressRequest, MyAddressRecord>("/api/me/addresses", request, token);

    public Task<MyAddressRecord?> UpdateMyAddressAsync(Guid id, UpsertMyAddressRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertMyAddressRequest, MyAddressRecord>($"/api/me/addresses/{id}", request, token);

    public Task<bool> DeleteMyAddressAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/me/addresses/{id}", token);

    public async Task<List<MyLinkRecord>> GetMyLinksAsync(CancellationToken token = default)
        => await _api.GetAsync<List<MyLinkRecord>>("/api/me/links", token) ?? [];

    public Task<MyLinkRecord?> CreateMyLinkAsync(UpsertMyLinkRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertMyLinkRequest, MyLinkRecord>("/api/me/links", request, token);

    public Task<MyLinkRecord?> UpdateMyLinkAsync(Guid id, UpsertMyLinkRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertMyLinkRequest, MyLinkRecord>($"/api/me/links/{id}", request, token);

    public Task<bool> DeleteMyLinkAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/me/links/{id}", token);

    public Task<EmailValidationInfoRecord?> GetEmailValidationInfoAsync(string token, CancellationToken cancellationToken = default)
        => _api.GetAnonymousAsync<EmailValidationInfoRecord>($"/api/public/email-validation/{Uri.EscapeDataString(token)}", cancellationToken);

    public Task<bool> ConfirmEmailValidationAsync(string token, CancellationToken cancellationToken = default)
        => _api.PostAnonymousVoidAsync<object?>($"/api/public/email-validation/{Uri.EscapeDataString(token)}", null, cancellationToken);

    public async Task<bool> ReviewPermissionRequestAsync(
        Guid requestId, FilePermissionRequestStatus status, string? reviewNotes, CancellationToken token = default)
        => await _api.PutAsync<ReviewPermissionRequestRequest, UploadFilePermissionRequestResponse>(
               $"/api/upload-file-permission-requests/{requestId}/review",
               new ReviewPermissionRequestRequest(status, reviewNotes), token) is not null;

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

    public async Task<IReadOnlyList<OrgUserDirectoryItem>> GetOrgUserDirectoryAsync(Guid organizationId, CancellationToken token = default)
    {
        var entries = await _api.GetOrgUserDirectoryAsync(organizationId, token);
        return entries.Select(e => new OrgUserDirectoryItem(e.Id, e.DisplayName)).ToList();
    }

    public Task<AppUserDetailAdminRecord?> GetUserDetailAsync(Guid userId, CancellationToken token = default)
        => _api.GetAsync<AppUserDetailAdminRecord>($"/api/admin/app-users/{userId}/detail", token);

    public Task<AppUserAdminRecord?> CreateUserAsync(AdminCreateUserRequest request, CancellationToken token = default)
        => _api.PostAsync<AdminCreateUserRequest, AppUserAdminRecord>("/api/admin/app-users", request, token);

    public Task<AppUserAdminRecord?> UpdateUserProfileAsync(Guid userId, AdminUpdateUserProfileRequest request, CancellationToken token = default)
        => _api.PutAsync<AdminUpdateUserProfileRequest, AppUserAdminRecord>($"/api/admin/app-users/{userId}/profile", request, token);

    public Task<bool> ImpersonateUserAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default)
        => _auth.ImpersonateAsync(targetUserId, targetUserEmail, token);

    public Task StopImpersonatingAsync(CancellationToken token = default)
        => _auth.StopImpersonatingAsync(token);

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

    public Task<OrgBrowsePage?> BrowseOrganizationsAsync(int page = 1, int pageSize = 24, CancellationToken token = default)
        => _api.GetAnonymousAsync<OrgBrowsePage>(
               $"/api/public/organizations/browse?page={page}&pageSize={pageSize}", token);

    // ── Support tickets ───────────────────────────────────────────────────────

    public Task<SiteContactInfo?> GetSiteContactAsync(CancellationToken token = default)
        => _api.GetAnonymousAsync<SiteContactInfo>("/api/public/site-contact", token);

    public Task<SupportFormTokenResponse?> GetSupportFormTokenAsync(CancellationToken token = default)
        => _api.GetAnonymousAsync<SupportFormTokenResponse>("/api/public/support-tickets/form-token", token);

    public Task<SubmitSupportTicketResponse?> SubmitSupportTicketAsync(SubmitSupportTicketRequest request, CancellationToken token = default)
        => _api.PostAnonymousAsync<SubmitSupportTicketRequest, SubmitSupportTicketResponse>("/api/public/support-tickets", request, token);

    public Task<SupportTicketPublicRecord?> GetSupportTicketByTokenAsync(Guid accessToken, CancellationToken token = default)
        => _api.GetAnonymousAsync<SupportTicketPublicRecord>($"/api/public/support-tickets/{accessToken}", token);

    public Task<bool> ReplyToSupportTicketByTokenAsync(Guid accessToken, AddSupportTicketReplyRequest request, CancellationToken token = default)
        => _api.PostAnonymousVoidAsync($"/api/public/support-tickets/{accessToken}/replies", request, token);

    public async Task<SupportTicketPage?> GetSupportTicketsAsync(SupportTicketStatus? status = null, SupportTicketTopic? topic = null, string? search = null, int page = 1, int pageSize = 25, CancellationToken token = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (status is not null) query.Add($"status={(int)status}");
        if (topic is not null) query.Add($"topic={(int)topic}");
        if (!string.IsNullOrWhiteSpace(search)) query.Add($"search={Uri.EscapeDataString(search)}");
        return await _api.GetAsync<SupportTicketPage>($"/api/admin/support-tickets?{string.Join("&", query)}", token);
    }

    public async Task<IReadOnlyList<SupportTicketReplyRecord>> GetSupportTicketRepliesAsync(Guid id, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<SupportTicketReplyRecord>>($"/api/admin/support-tickets/{id}/replies", token) ?? [];

    public Task<bool> AddSupportTicketReplyAsync(Guid id, AddSupportTicketReplyRequest request, CancellationToken token = default)
        => _api.PostVoidAsync($"/api/admin/support-tickets/{id}/replies", request, token);

    public Task<SupportTicketAdminRecord?> UpdateSupportTicketAsync(Guid id, UpdateSupportTicketRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateSupportTicketRequest, SupportTicketAdminRecord>($"/api/admin/support-tickets/{id}", request, token);

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
        Guid orgId, Guid requestId, OrganizationMembershipRequestStatus status, string? responseNote,
        bool? canReapply = null, string? denialReason = null, CancellationToken token = default)
        => _api.PutAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/respond",
               new { Status = status, ResponseNote = responseNote, CanReapply = canReapply, DenialReason = denialReason }, token);

    public Task<bool> WithdrawMembershipRequestAsync(Guid orgId, Guid requestId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/membership-requests/{requestId}", token);

    // ── Membership Questions (Phase 3) ────────────────────────────────────────

    public async Task<IReadOnlyList<OrganizationMembershipQuestionRecord>> GetMembershipQuestionsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrganizationMembershipQuestionRecord>>($"/api/organizations/{orgId}/membership-questions", token);
        return result ?? [];
    }

    public Task<OrganizationMembershipQuestionRecord?> CreateMembershipQuestionAsync(Guid orgId, UpsertMembershipQuestionRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>($"/api/organizations/{orgId}/membership-questions", request, token);

    public Task<OrganizationMembershipQuestionRecord?> UpdateMembershipQuestionAsync(Guid orgId, Guid id, UpsertMembershipQuestionRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertMembershipQuestionRequest, OrganizationMembershipQuestionRecord>($"/api/organizations/{orgId}/membership-questions/{id}", request, token);

    public Task<bool> DeleteMembershipQuestionAsync(Guid orgId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/membership-questions/{id}", token);

    // ── Membership Voting (Phase 3) ───────────────────────────────────────────

    public Task<OrganizationMembershipRequestRecord?> OpenMembershipVoteAsync(Guid orgId, Guid requestId, DateTime voteDeadline, CancellationToken token = default)
        => _api.PostAsync<object, OrganizationMembershipRequestRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/open-vote",
               new { VoteDeadline = voteDeadline }, token);

    public Task<MembershipReviewVoteRecord?> CastMembershipVoteAsync(Guid orgId, Guid requestId, Ben.Data.Common.Enums.MembershipVoteType voteType, string? comment, CancellationToken token = default)
        => _api.PostAsync<object, MembershipReviewVoteRecord>(
               $"/api/organizations/{orgId}/membership-requests/{requestId}/vote",
               new { VoteType = voteType, Comment = comment }, token);

    public async Task<IReadOnlyList<MembershipReviewVoteRecord>> GetMembershipVotesAsync(Guid orgId, Guid requestId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<MembershipReviewVoteRecord>>($"/api/organizations/{orgId}/membership-requests/{requestId}/votes", token);
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

    // ── Case Transfers ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseTransferLogRecord>> GetCaseTransfersAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseTransferLogRecord>>(
            $"/api/organizations/{orgId}/cases/{caseId}/transfers", token);
        return result ?? [];
    }

    public Task<CaseTransferLogRecord?> ProposeCaseTransferAsync(Guid orgId, Guid caseId, Guid toOrganizationId, string? reason, CancellationToken token = default)
        => _api.PostAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers",
               new { ToOrganizationId = toOrganizationId, TransferReason = reason }, token);

    public Task<CaseTransferLogRecord?> RespondCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, bool accept, string? rejectionReason, CancellationToken token = default)
        => _api.PutAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/respond",
               new { Accept = accept, Reason = rejectionReason }, token);

    public Task<CaseTransferLogRecord?> CancelCaseTransferAsync(Guid orgId, Guid caseId, Guid logId, CancellationToken token = default)
        => _api.PutAsync<object, CaseTransferLogRecord>(
               $"/api/organizations/{orgId}/cases/{caseId}/transfers/{logId}/cancel",
               new { }, token);

    // ── Public Case Discovery ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<PublicCaseListItem>> GetPublicCasesAsync(string orgUrlName, CancellationToken token = default)
    {
        var result = await _api.GetAnonymousAsync<IReadOnlyList<PublicCaseListItem>>(
            $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/cases", token);
        return result ?? [];
    }

    public Task<PublicCaseDetail?> GetPublicCaseAsync(string orgUrlName, string caseRef, CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicCaseDetail>(
               $"/api/public/organizations/{Uri.EscapeDataString(orgUrlName)}/cases/{Uri.EscapeDataString(caseRef)}", token);

    public Task<PublicCaseDiscoveryPagedResponse?> GetPublicCaseDiscoveryAsync(int page = 1, int pageSize = 20, string sort = "votes", CancellationToken token = default)
        => _api.GetAnonymousAsync<PublicCaseDiscoveryPagedResponse>($"/api/public/cases?page={page}&pageSize={pageSize}&sort={Uri.EscapeDataString(sort)}", token);

    // ── Case votes ────────────────────────────────────────────────────────────

    public Task<CaseVoteSummary?> GetCaseVoteSummaryAsync(Guid caseId, CancellationToken token = default)
        => _api.GetAnonymousAsync<CaseVoteSummary>($"/api/public/cases/{caseId}/votes", token);

    public async Task<IReadOnlyList<CaseVoteSummary>> GetCaseVoteSummariesAsync(IEnumerable<Guid> caseIds, CancellationToken token = default)
    {
        var qs = string.Join("&", caseIds.Select(id => $"caseIds={id}"));
        if (string.IsNullOrEmpty(qs)) return [];
        var result = await _api.GetAnonymousAsync<IReadOnlyList<CaseVoteSummary>>(
            $"/api/public/cases/vote-summaries?{qs}", token);
        return result ?? [];
    }

    public Task<CaseVoteSummary?> CastCaseVoteAsync(Guid caseId, Ben.Data.Common.Enums.EvidenceVoteType voteType, CancellationToken token = default)
        => _api.PostAsync<object, CaseVoteSummary>($"/api/public/cases/{caseId}/votes", new { VoteType = voteType }, token);

    public Task<bool> RemoveCaseVoteAsync(Guid caseId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/public/cases/{caseId}/votes", token);

    // ── Investigations ────────────────────────────────────────────────────────

    private static string InvBase(Guid orgId, Guid caseId)
        => $"/api/organizations/{orgId}/cases/{caseId}/investigations";

    public async Task<IReadOnlyList<InvestigationRecord>> GetInvestigationsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<InvestigationRecord>>(InvBase(orgId, caseId), token);
        return result ?? [];
    }

    public Task<InvestigationRecord?> GetInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.GetAsync<InvestigationRecord>($"{InvBase(orgId, caseId)}/{id}", token);

    public Task<InvestigationRecord?> CreateInvestigationAsync(Guid orgId, Guid caseId, UpsertInvestigationRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertInvestigationRequest, InvestigationRecord>(InvBase(orgId, caseId), request, token);

    public Task<InvestigationRecord?> UpdateInvestigationAsync(Guid orgId, Guid caseId, Guid id, UpsertInvestigationRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertInvestigationRequest, InvestigationRecord>($"{InvBase(orgId, caseId)}/{id}", request, token);

    public Task<bool> DeleteInvestigationAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"{InvBase(orgId, caseId)}/{id}", token);

    public Task<bool> CancelInvestigationByOrgAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
        => _api.PostVoidAsync($"{InvBase(orgId, caseId)}/{id}/cancel", new { }, token);

    public async Task<IReadOnlyList<InvestigationAttendeeRecord>> GetInvestigationAttendeesAsync(Guid orgId, Guid caseId, Guid id, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<InvestigationAttendeeRecord>>($"{InvBase(orgId, caseId)}/{id}/attendees", token);
        return result ?? [];
    }

    public Task<InvestigationAttendeeRecord?> AddInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, AddInvestigationAttendeeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddInvestigationAttendeeRequest, InvestigationAttendeeRecord>($"{InvBase(orgId, caseId)}/{id}/attendees", request, token);

    public Task<InvestigationAttendeeRecord?> UpdateInvestigationAttendanceAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, bool? didAttend, string? assignedRole, Ben.Data.Common.Enums.RsvpStatus? rsvp = null, CancellationToken token = default)
        => _api.PutAsync<object, InvestigationAttendeeRecord>(
               $"{InvBase(orgId, caseId)}/{id}/attendees/{attendeeId}/attendance",
               new { DidAttend = didAttend, AssignedRole = assignedRole, Rsvp = rsvp }, token);

    public Task<bool> RemoveInvestigationAttendeeAsync(Guid orgId, Guid caseId, Guid id, Guid attendeeId, CancellationToken token = default)
        => _api.DeleteAsync($"{InvBase(orgId, caseId)}/{id}/attendees/{attendeeId}", token);

    // ── Evidence Voting ───────────────────────────────────────────────────────

    public Task<EvidenceVoteSummary?> GetEvidenceVoteSummaryAsync(Guid uploadFileId, CancellationToken token = default)
        => _api.GetAnonymousAsync<EvidenceVoteSummary>($"/api/evidence-votes/{uploadFileId}/summary", token);

    public async Task<IReadOnlyList<EvidenceVoteRecord>> GetEvidenceVotesAsync(Guid uploadFileId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<EvidenceVoteRecord>>($"/api/evidence-votes/{uploadFileId}", token);
        return result ?? [];
    }

    public Task<EvidenceVoteSummary?> CastEvidenceVoteAsync(Guid uploadFileId, Ben.Data.Common.Enums.EvidenceVoteType voteType, string? comment, CancellationToken token = default)
        => _api.PostAsync<object, EvidenceVoteSummary>(
               $"/api/evidence-votes/{uploadFileId}",
               new { VoteType = voteType, Comment = comment }, token);

    public Task<bool> RemoveEvidenceVoteAsync(Guid uploadFileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/evidence-votes/{uploadFileId}", token);

    // ── Messaging ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrgMessageRecord>> GetOrgInboxAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgMessageRecord>>($"/api/organizations/{orgId}/messages/inbox", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<OrgMessageRecord>> GetOrgSentAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgMessageRecord>>($"/api/organizations/{orgId}/messages/sent", token);
        return result ?? [];
    }

    public Task<OrgMessageRecord?> GetOrgMessageAsync(Guid orgId, Guid messageId, CancellationToken token = default)
        => _api.GetAsync<OrgMessageRecord>($"/api/organizations/{orgId}/messages/{messageId}", token);

    public Task<OrgMessageRecord?> SendOrgMessageAsync(Guid orgId, SendOrgMessageRequest request, CancellationToken token = default)
        => _api.PostAsync<SendOrgMessageRequest, OrgMessageRecord>($"/api/organizations/{orgId}/messages", request, token);

    // ── Org-wide investigations (Area 9) ──────────────────────────────────────

    public async Task<IReadOnlyList<OrgInvestigationRow>> GetOrgInvestigationsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgInvestigationRow>>($"/api/organizations/{orgId}/investigations", token);
        return result ?? [];
    }

    public Task<InvestigationRecord?> CreateOrgInvestigationAsync(
        Guid orgId, CreateOrgInvestigationRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateOrgInvestigationRequest, InvestigationRecord>(
            $"/api/organizations/{orgId}/investigations", request, token);

    // ── Calendar ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<OrgCalendarEventTypeRecord>> GetCalendarEventTypesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgCalendarEventTypeRecord>>($"/api/organizations/{orgId}/calendar-event-types", token);
        return result ?? [];
    }

    public Task<OrgCalendarEventTypeRecord?> CreateCalendarEventTypeAsync(Guid orgId, UpsertCalendarEventTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types", request, token);

    public Task<OrgCalendarEventTypeRecord?> UpdateCalendarEventTypeAsync(Guid orgId, Guid id, UpsertCalendarEventTypeRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCalendarEventTypeRequest, OrgCalendarEventTypeRecord>($"/api/organizations/{orgId}/calendar-event-types/{id}", request, token);

    public Task<bool> DeleteCalendarEventTypeAsync(Guid orgId, Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar-event-types/{id}", token);

    public async Task<IReadOnlyList<OrgCalendarEventRecord>> GetCalendarEventsAsync(Guid orgId, DateTime? from = null, DateTime? to = null, CancellationToken token = default)
    {
        var qs = string.Empty;
        if (from.HasValue) qs += $"?from={Uri.EscapeDataString(from.Value.ToString("o"))}";
        if (to.HasValue)   qs += (qs.Length > 0 ? "&" : "?") + $"to={Uri.EscapeDataString(to.Value.ToString("o"))}";
        var result = await _api.GetAsync<IReadOnlyList<OrgCalendarEventRecord>>($"/api/organizations/{orgId}/calendar{qs}", token);
        return result ?? [];
    }

    public Task<OrgCalendarEventRecord?> GetCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.GetAsync<OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar/{eventId}", token);

    public Task<OrgCalendarEventRecord?> CreateCalendarEventAsync(Guid orgId, UpsertCalendarEventRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar", request, token);

    public Task<OrgCalendarEventRecord?> UpdateCalendarEventAsync(Guid orgId, Guid eventId, UpsertCalendarEventRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCalendarEventRequest, OrgCalendarEventRecord>($"/api/organizations/{orgId}/calendar/{eventId}", request, token);

    public Task<bool> DeleteCalendarEventAsync(Guid orgId, Guid eventId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}", token);

    public async Task<IReadOnlyList<OrgCalendarEventAttendeeRecord>> GetCalendarEventAttendeesAsync(Guid orgId, Guid eventId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgCalendarEventAttendeeRecord>>($"/api/organizations/{orgId}/calendar/{eventId}/attendees", token);
        return result ?? [];
    }

    public Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeAsync(Guid orgId, Guid eventId, AddAttendeeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddAttendeeRequest, OrgCalendarEventAttendeeRecord>($"/api/organizations/{orgId}/calendar/{eventId}/attendees", request, token);

    public Task<OrgCalendarEventAttendeeRecord?> AddCalendarAttendeeByEmailAsync(Guid orgId, Guid eventId, string email, CancellationToken token = default)
        => _api.PostAsync<AddAttendeeByEmailRequest, OrgCalendarEventAttendeeRecord>(
               $"/api/organizations/{orgId}/calendar/{eventId}/attendees/by-email",
               new AddAttendeeByEmailRequest(email), token);

    public Task<OrgCalendarEventAttendeeRecord?> RsvpCalendarEventAsync(Guid orgId, Guid eventId, Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus status, CancellationToken token = default)
        => _api.PutAsync<object, OrgCalendarEventAttendeeRecord>(
               $"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}/rsvp",
               new { RsvpStatus = status }, token);

    public Task<bool> RemoveCalendarAttendeeAsync(Guid orgId, Guid eventId, Guid attendeeId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/calendar/{eventId}/attendees/{attendeeId}", token);

    // ── Cases ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseRecord>> GetOrgCasesAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseRecord>>($"/api/organizations/{orgId}/cases", token);
        return result ?? [];
    }

    public Task<CaseRecord?> GetOrgCaseAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseRecord>($"/api/organizations/{orgId}/cases/{caseId}", token);

    public Task<CaseClientRequestRecord?> GetOrgCaseClientRequestAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseClientRequestRecord>($"/api/organizations/{orgId}/cases/{caseId}/client-request", token);

    public Task<CaseRecord?> CreateOrgCaseAsync(Guid orgId, CreateCaseRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateCaseRequest, CaseRecord>($"/api/organizations/{orgId}/cases", request, token);

    public async Task<IReadOnlyList<OrgPendingRequestRecord>> GetOrgPendingRequestsAsync(Guid orgId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<OrgPendingRequestRecord>>($"/api/organizations/{orgId}/cases/pending-requests", token);
        return result ?? [];
    }

    public Task<CaseRecord?> AcceptClientRequestAsCaseAsync(Guid orgId, Guid clientRequestId, AcceptClientRequestAsCaseRequest request, CancellationToken token = default)
        => _api.PostAsync<AcceptClientRequestAsCaseRequest, CaseRecord>(
               $"/api/organizations/{orgId}/cases/accept-client-request/{clientRequestId}", request, token);

    public Task<bool> DeclineClientRequestAsync(Guid orgId, Guid clientRequestId, CancellationToken token = default)
        => _api.PostVoidAsync(
               $"/api/organizations/{orgId}/cases/decline-request/{clientRequestId}", new { }, token);

    public Task<bool> UpdatePendingRequestStatusAsync(Guid orgId, Guid clientRequestId, Ben.Data.Common.Enums.ClientOrgRequestStatus status, CancellationToken token = default)
        => _api.PutVoidAsync(
               $"/api/organizations/{orgId}/cases/request-status/{clientRequestId}",
               new { Status = (int)status }, token);

    public Task<CaseRecord?> UpdateOrgCaseAsync(Guid orgId, Guid caseId, UpdateCaseRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateCaseRequest, CaseRecord>($"/api/organizations/{orgId}/cases/{caseId}", request, token);

    public async Task<IReadOnlyList<CaseTimelineEntryRecord>> GetCaseTimelineAsync(Guid orgId, Guid caseId, Guid? investigationId = null, CancellationToken token = default)
    {
        var url = $"/api/organizations/{orgId}/cases/{caseId}/timeline";
        if (investigationId is { } id) url += $"?investigationId={id}";
        var result = await _api.GetAsync<IReadOnlyList<CaseTimelineEntryRecord>>(url, token);
        return result ?? [];
    }

    public Task<CaseTimelineEntryRecord?> AddCaseTimelineEntryAsync(Guid orgId, Guid caseId, UpsertTimelineEntryRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>($"/api/organizations/{orgId}/cases/{caseId}/timeline", request, token);

    public Task<CaseTimelineEntryRecord?> UpdateCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, UpsertTimelineEntryRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertTimelineEntryRequest, CaseTimelineEntryRecord>($"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}", request, token);

    public Task<bool> DeleteCaseTimelineEntryAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cases/{caseId}/timeline/{entryId}", token);

    // ── Case Report Builder ───────────────────────────────────────────────────

    // Client-facing: published reports only
    public async Task<IReadOnlyList<CaseReportSummary>> GetMyCaseReportsAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseReportSummary>>($"/api/my-cases/{caseId}/reports", token) ?? [];

    public string GetMyCaseReportPdfUrl(Guid caseId, Guid reportId)
        => $"/api/my-cases/{caseId}/reports/{reportId}/pdf";

    // Org-facing
    public async Task<IReadOnlyList<CaseReportSummary>> GetCaseReportsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseReportSummary>>($"/api/orgs/{orgId}/cases/{caseId}/reports", token) ?? [];

    public Task<CaseReportDetail?> GetCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
        => _api.GetAsync<CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}", token);

    public Task<CaseReportDetail?> CreateCaseReportAsync(Guid orgId, Guid caseId, UpsertCaseReportRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertCaseReportRequest, CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports", request, token);

    public Task<CaseReportDetail?> UpdateCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, UpsertCaseReportRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertCaseReportRequest, CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}", request, token);

    public Task<CaseReportDetail?> PublishCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
        => _api.PostAsync<object, CaseReportDetail>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/publish", new { }, token);

    public Task<bool> DeleteCaseReportAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}", token);

    public Task<CaseReportSectionDto?> AddReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, UpsertSectionRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertSectionRequest, CaseReportSectionDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections", request, token);

    public Task<CaseReportSectionDto?> UpdateReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, UpsertSectionRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertSectionRequest, CaseReportSectionDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}", request, token);

    public Task<bool> DeleteReportSectionAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}", token);

    public Task<CaseReportSectionFileDto?> AddReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid uploadFileId, string? caption, CancellationToken token = default)
        => _api.PostAsync<object, CaseReportSectionFileDto>($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}/files", new { UploadFileId = uploadFileId, Caption = caption }, token);

    public Task<bool> RemoveReportSectionFileAsync(Guid orgId, Guid caseId, Guid reportId, Guid sectionId, Guid fileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/sections/{sectionId}/files/{fileId}", token);

    public string GetReportPdfUrl(Guid orgId, Guid caseId, Guid reportId)
        => $"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/pdf";

    public async Task<(byte[] Data, string FileName)?> DownloadCaseReportPdfAsync(Guid orgId, Guid caseId, Guid reportId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/orgs/{orgId}/cases/{caseId}/reports/{reportId}/pdf", "report.pdf", token);
        return result is null ? null : (result.Value.Data, result.Value.FileName);
    }

    public async Task<(byte[] Data, string FileName)?> DownloadMyCaseReportPdfAsync(Guid caseId, Guid reportId, CancellationToken token = default)
    {
        var result = await _api.GetBytesAsync($"/api/my-cases/{caseId}/reports/{reportId}/pdf", "report.pdf", token);
        return result is null ? null : (result.Value.Data, result.Value.FileName);
    }

    // ── Case Research ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseResearchEntryDto>> GetCaseResearchAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseResearchEntryDto>>($"/api/orgs/{orgId}/cases/{caseId}/research", token) ?? [];

    public Task<CaseResearchEntryDto?> AddCaseResearchAsync(Guid orgId, Guid caseId, UpsertResearchRequest request, CancellationToken token = default)
        => _api.PostAsync<UpsertResearchRequest, CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research", request, token);

    public async Task<CaseResearchEntryDto?> UploadCaseResearchFileAsync(Guid orgId, Guid caseId, string title, string? description, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(title), "title");
        if (description is not null) form.Add(new StringContent(description), "description");
        using var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        return await _api.PostMultipartAsync<CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research/files", form, token);
    }

    public Task<CaseResearchEntryDto?> UpdateCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, UpsertResearchRequest request, CancellationToken token = default)
        => _api.PutAsync<UpsertResearchRequest, CaseResearchEntryDto>($"/api/orgs/{orgId}/cases/{caseId}/research/{entryId}", request, token);

    public Task<bool> DeleteCaseResearchAsync(Guid orgId, Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/research/{entryId}", token);

    // ── Case Files (Files/Evidence tab) ──────────────────────────────────────

    public async Task<IReadOnlyList<CaseFileRecord>> GetCaseFilesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseFileRecord>>($"/api/orgs/{orgId}/cases/{caseId}/files", token) ?? [];

    public async Task<CaseFileRecord?> UploadCaseFileAsync(Guid orgId, Guid caseId, string? description, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        if (description is not null) form.Add(new StringContent(description), "description");
        using var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        return await _api.PostMultipartAsync<CaseFileRecord>($"/api/orgs/{orgId}/cases/{caseId}/files", form, token);
    }

    public Task<bool> DeleteCaseFileAsync(Guid orgId, Guid caseId, Guid caseFileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/files/{caseFileId}", token);

    public Task<CaseFileRecord?> LinkCaseFileAsync(Guid orgId, Guid caseId, Guid uploadFileId, string? description = null, CancellationToken token = default)
        => _api.PostAsync<LinkCaseFileRequest, CaseFileRecord>(
            $"/api/orgs/{orgId}/cases/{caseId}/files/link/{uploadFileId}", new LinkCaseFileRequest(description), token);

    public Task<CaseFileRecord?> ExportAudioMixAsync(Guid orgId, Guid caseId, ExportAudioMixRequest request, CancellationToken token = default)
        => _api.PostAsync<ExportAudioMixRequest, CaseFileRecord>($"/api/orgs/{orgId}/cases/{caseId}/audio-mix/export", request, token);

    // ── Case Notes ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseNoteDto>> GetCaseNotesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseNoteDto>>($"/api/organizations/{orgId}/cases/{caseId}/notes", token) ?? [];

    public Task<CaseNoteDto?> CreateCaseNoteAsync(Guid orgId, Guid caseId, UpsertCaseNoteDto request, CancellationToken token = default)
        => _api.PostAsync<UpsertCaseNoteDto, CaseNoteDto>($"/api/organizations/{orgId}/cases/{caseId}/notes", request, token);

    public Task<CaseNoteDto?> UpdateCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, UpsertCaseNoteDto request, CancellationToken token = default)
        => _api.PutAsync<UpsertCaseNoteDto, CaseNoteDto>($"/api/organizations/{orgId}/cases/{caseId}/notes/{noteId}", request, token);

    public Task<bool> DeleteCaseNoteAsync(Guid orgId, Guid caseId, Guid noteId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/organizations/{orgId}/cases/{caseId}/notes/{noteId}", token);

    // ── Investigation Scheduling ──────────────────────────────────────────────

    public Task<bool> CancelMyInvestigationAsync(Guid caseId, Guid investigationId, CancellationToken token = default)
        => _api.PostAsync<object, object>($"/api/my-cases/{caseId}/investigations/{investigationId}/cancel", new { }, token)
               .ContinueWith(t => t.Result is not null);

    public async Task<IReadOnlyList<ScheduleProposalDto>> GetScheduleProposalsAsync(Guid orgId, Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<ScheduleProposalDto>>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals", token) ?? [];

    public Task<ScheduleProposalDto?> CreateScheduleProposalAsync(Guid orgId, Guid caseId, CreateProposalRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateProposalRequest, ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals", request, token);

    public Task<bool> WithdrawScheduleProposalAsync(Guid orgId, Guid caseId, Guid proposalId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals/{proposalId}", token);

    public Task<ScheduleProposalDto?> ConvertProposalToInvestigationAsync(Guid orgId, Guid caseId, Guid proposalId, ConvertProposalRequest request, CancellationToken token = default)
        => _api.PostAsync<ConvertProposalRequest, ScheduleProposalDto>($"/api/orgs/{orgId}/cases/{caseId}/schedule-proposals/{proposalId}/convert", request, token);

    public async Task<IReadOnlyList<ScheduleProposalDto>> GetMyScheduleProposalsAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<ScheduleProposalDto>>($"/api/my-cases/{caseId}/schedule-proposals", token) ?? [];

    public Task<ScheduleProposalDto?> AcceptScheduleProposalAsync(Guid caseId, Guid proposalId, Guid slotId, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/accept", new { SlotId = slotId }, token);

    public Task<ScheduleProposalDto?> CounterScheduleProposalAsync(Guid caseId, Guid proposalId, DateTime preferredDateTime, string? notes, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/counter", new { PreferredDateTime = preferredDateTime, Notes = notes }, token);

    public Task<ScheduleProposalDto?> DeclineScheduleProposalAsync(Guid caseId, Guid proposalId, string? notes, CancellationToken token = default)
        => _api.PostAsync<object, ScheduleProposalDto>($"/api/my-cases/{caseId}/schedule-proposals/{proposalId}/decline", new { Notes = notes }, token);

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

    public Task<ClientRequestRecord?> AddOrganizationToRequestAsync(Guid id, Guid organizationId, CancellationToken token = default)
        => _api.PostAsync<object, ClientRequestRecord>($"/api/client-requests/{id}/add-organization",
               new { OrganizationId = organizationId }, token);

    // ── My Cases ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ClientCaseListItem>> GetMyCasesAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<ClientCaseListItem>>("/api/my-cases", token);
        return result ?? [];
    }

    public Task<ClientCaseDetail?> GetMyCaseAsync(Guid caseId, CancellationToken token = default)
        => _api.GetAsync<ClientCaseDetail>($"/api/my-cases/{caseId}", token);

    public Task<CaseTimelineEntryRecord?> LogOccurrenceAsync(Guid caseId, LogOccurrenceRequest request, CancellationToken token = default)
        => _api.PostAsync<LogOccurrenceRequest, CaseTimelineEntryRecord>($"/api/my-cases/{caseId}/occurrences", request, token);

    public Task<CaseTimelineEntryRecord?> UpdateOccurrenceAsync(Guid caseId, Guid entryId, LogOccurrenceRequest request, CancellationToken token = default)
        => _api.PutAsync<LogOccurrenceRequest, CaseTimelineEntryRecord>($"/api/my-cases/{caseId}/occurrences/{entryId}", request, token);

    public Task<bool> DeleteOccurrenceAsync(Guid caseId, Guid entryId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/occurrences/{entryId}", token);

    public async Task<OccurrenceFileItem?> AttachOccurrenceFileAsync(
        Guid caseId, Guid entryId, Stream content, string fileName, string contentType, CancellationToken token = default)
    {
        using var form = new MultipartFormDataContent();
        using var sc   = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        return await _api.PostMultipartAsync<OccurrenceFileItem>(
            $"/api/my-cases/{caseId}/occurrences/{entryId}/files", form, token);
    }

    public Task<bool> DetachOccurrenceFileAsync(Guid caseId, Guid entryId, Guid fileId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/occurrences/{entryId}/files/{fileId}", token);

    public async Task<IReadOnlyList<CaseMessageRecord>> GetMyCaseMessagesAsync(Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseMessageRecord>>($"/api/my-cases/{caseId}/messages", token);
        return result ?? [];
    }

    public Task<CaseMessageRecord?> PostMyCaseMessageAsync(Guid caseId, string body, CancellationToken token = default)
        => _api.PostAsync<object, CaseMessageRecord>($"/api/my-cases/{caseId}/messages", new { Body = body }, token);

    // ── Co-client access management ───────────────────────────────────────────

    public async Task<IReadOnlyList<CoClientItem>> GetCoClientsAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CoClientItem>>($"/api/my-cases/{caseId}/co-clients", token) ?? [];

    public Task<CoClientItem?> AddCoClientAsync(Guid caseId, string email, CancellationToken token = default)
        => _api.PostAsync<object, CoClientItem>($"/api/my-cases/{caseId}/co-clients", new { Email = email }, token);

    public Task<bool> RemoveCoClientAsync(Guid caseId, Guid accessId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/co-clients/{accessId}", token);

    // ── Sub-client invites (item #4) ──────────────────────────────────────────

    public async Task<IReadOnlyList<CaseClientInviteRecord>> GetCaseInvitesAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseClientInviteRecord>>($"/api/my-cases/{caseId}/invites", token) ?? [];

    public Task<InviteCoClientResult?> InviteCoClientAsync(Guid caseId, string email, CancellationToken token = default)
        => _api.PostAsync<object, InviteCoClientResult>($"/api/my-cases/{caseId}/invites", new { Email = email }, token);

    public Task<bool> RevokeCaseInviteAsync(Guid caseId, Guid inviteId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/invites/{inviteId}", token);

    // ── Related people (basic-info, no account) ─────────────────────────────────

    public async Task<IReadOnlyList<CaseRelatedPersonRecord>> GetRelatedPeopleAsync(Guid caseId, CancellationToken token = default)
        => await _api.GetAsync<IReadOnlyList<CaseRelatedPersonRecord>>($"/api/my-cases/{caseId}/related-people", token) ?? [];

    public Task<CaseRelatedPersonRecord?> AddRelatedPersonAsync(Guid caseId, AddRelatedPersonRequest request, CancellationToken token = default)
        => _api.PostAsync<AddRelatedPersonRequest, CaseRelatedPersonRecord>($"/api/my-cases/{caseId}/related-people", request, token);

    public Task<Ben.Data.Common.Enums.HelpAudience?> GetMyHelpAudienceAsync(CancellationToken token = default)
        => _api.GetAsync<Ben.Data.Common.Enums.HelpAudience?>("/api/me/help-audience", token);

    public async Task<List<VideoAssetAdminRecord>> GetVideoAssetsAsync(CancellationToken token = default)
        => await _api.GetAsync<List<VideoAssetAdminRecord>>("/api/admin/video-assets", token) ?? [];

    public Task<VideoAssetAdminRecord?> CreateVideoAssetAsync(
        CreateVideoAssetRequest request, CancellationToken token = default)
        => _api.PostAsync<CreateVideoAssetRequest, VideoAssetAdminRecord>(
               "/api/admin/video-assets", request, token);

    public Task<VideoAssetAdminRecord?> UpdateVideoAssetAsync(
        Guid id, UpdateVideoAssetRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateVideoAssetRequest, VideoAssetAdminRecord>(
               $"/api/admin/video-assets/{id}", request, token);

    public Task<bool> RetireVideoAssetAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/admin/video-assets/{id}", token);

    public async Task<List<SiteSettingRecord>> GetSiteSettingsAsync(CancellationToken token = default)
        => await _api.GetAsync<List<SiteSettingRecord>>("/api/admin/site-settings", token) ?? [];

    public Task<SiteSettingRecord?> SetSiteSettingAsync(
        string key, SetSiteSettingRequest request, CancellationToken token = default)
        => _api.PutAsync<SetSiteSettingRequest, SiteSettingRecord>(
               $"/api/admin/site-settings/{Uri.EscapeDataString(key)}", request, token);

    public Task<CaseDisplayAliasRecord?> GetCaseDisplayAliasAsync(Guid caseId, CancellationToken token = default)
        => _api.GetAsync<CaseDisplayAliasRecord>($"/api/my-cases/{caseId}/display-alias", token);

    public Task<CaseDisplayAliasRecord?> SetCaseDisplayAliasAsync(
        Guid caseId, SetCaseDisplayAliasRequest request, CancellationToken token = default)
        => _api.PutAsync<SetCaseDisplayAliasRequest, CaseDisplayAliasRecord>(
               $"/api/my-cases/{caseId}/display-alias", request, token);

    public Task<CaseRelatedPersonRecord?> UpdateRelatedPersonAsync(
        Guid caseId, Guid personId, UpdateRelatedPersonRequest request, CancellationToken token = default)
        => _api.PutAsync<UpdateRelatedPersonRequest, CaseRelatedPersonRecord>(
               $"/api/my-cases/{caseId}/related-people/{personId}", request, token);

    public Task<bool> RemoveRelatedPersonAsync(Guid caseId, Guid personId, CancellationToken token = default)
        => _api.DeleteAsync($"/api/my-cases/{caseId}/related-people/{personId}", token);

    // ── My Investigations ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<MyInvestigationItem>> GetMyInvestigationsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<MyInvestigationItem>>("/api/my-investigations", token);
        return result ?? [];
    }

    public async Task<IReadOnlyList<AttendedInvestigationItem>> GetAttendedInvestigationsAsync(CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<AttendedInvestigationItem>>("/api/my-investigations/attended", token);
        return result ?? [];
    }

    public async Task UpdateMyInvestigationRsvpAsync(Guid attendeeId, Ben.Data.Common.Enums.RsvpStatus rsvp, CancellationToken token = default)
        => await _api.PutVoidAsync($"/api/my-investigations/{attendeeId}/rsvp", new { Rsvp = rsvp }, token);

    // ── Case Messages (org side) ──────────────────────────────────────────────

    public async Task<IReadOnlyList<CaseMessageRecord>> GetCaseMessagesAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<IReadOnlyList<CaseMessageRecord>>($"/api/orgs/{orgId}/cases/{caseId}/messages", token);
        return result ?? [];
    }

    public Task<CaseMessageRecord?> PostCaseMessageAsync(Guid orgId, Guid caseId, string body, CancellationToken token = default)
        => _api.PostAsync<object, CaseMessageRecord>($"/api/orgs/{orgId}/cases/{caseId}/messages", new { Body = body }, token);

    public async Task<int> GetCaseMessageUnreadCountAsync(Guid orgId, Guid caseId, CancellationToken token = default)
    {
        var result = await _api.GetAsync<int>($"/api/orgs/{orgId}/cases/{caseId}/messages/unread-count", token);
        return result;
    }

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

    public Task<RejectExperienceTypeResponse?> RejectExperienceTypeAsync(Guid categoryId, Guid id, CancellationToken token = default)
        => _api.PutAsync<object, RejectExperienceTypeResponse>($"/api/admin/experience-categories/{categoryId}/types/{id}/reject", new { }, token);

    public Task<ExperienceTypeRecord?> AddOrgExperienceTypeAsync(Guid orgId, AddOrgExperienceTypeRequest request, CancellationToken token = default)
        => _api.PostAsync<AddOrgExperienceTypeRequest, ExperienceTypeRecord>($"/api/organizations/{orgId}/experience-types", request, token);

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

    // ── File Comments (item #6 phase 2) ───────────────────────────────────────

    public Task<IReadOnlyList<UploadFileCommentRecord>> GetFileCommentsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetFileCommentsAsync(fileId, token);

    public Task<UploadFileCommentRecord?> CreateFileCommentAsync(Guid fileId, CreateFileCommentRequest request, CancellationToken token = default)
        => _api.CreateFileCommentAsync(fileId, request, token);

    public Task<UploadFileCommentRecord?> UpdateFileCommentAsync(Guid fileId, Guid commentId, UpdateFileCommentRequest request, CancellationToken token = default)
        => _api.UpdateFileCommentAsync(fileId, commentId, request, token);

    public Task<bool> DeleteFileCommentAsync(Guid fileId, Guid commentId, CancellationToken token = default)
        => _api.DeleteFileCommentAsync(fileId, commentId, token);

    public Task<FileCommentSettingsRecord?> GetFileCommentSettingsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetFileCommentSettingsAsync(fileId, token);

    public Task<FileCommentSettingsRecord?> UpdateFileCommentSettingsAsync(Guid fileId, FileCommentSettingsRecord request, CancellationToken token = default)
        => _api.UpdateFileCommentSettingsAsync(fileId, request, token);

    // ── Audio Markers (EVP) ──────────────────────────────────────────────────

    public Task<IReadOnlyList<AudioMarkerRecord>> GetAudioMarkersAsync(Guid fileId, CancellationToken token = default)
        => _api.GetAudioMarkersAsync(fileId, token);

    public Task<AudioMarkerRecord?> CreateAudioMarkerAsync(Guid fileId, CreateAudioMarkerRequest request, CancellationToken token = default)
        => _api.CreateAudioMarkerAsync(fileId, request, token);

    public Task<AudioMarkerRecord?> UpdateAudioMarkerAsync(Guid fileId, Guid markerId, UpdateAudioMarkerRequest request, CancellationToken token = default)
        => _api.UpdateAudioMarkerAsync(fileId, markerId, request, token);

    public Task<bool> DeleteAudioMarkerAsync(Guid fileId, Guid markerId, CancellationToken token = default)
        => _api.DeleteAudioMarkerAsync(fileId, markerId, token);

    public Task<IReadOnlyList<AudioMarkerRecord>> ReplaceAudioCandidatesAsync(Guid fileId, BulkCreateAudioCandidatesRequest request, CancellationToken token = default)
        => _api.ReplaceAudioCandidatesAsync(fileId, request, token);

    public Task<AudioMarkerRecord?> ReviewAudioMarkerAsync(Guid fileId, Guid markerId, ReviewAudioMarkerRequest request, CancellationToken token = default)
        => _api.ReviewAudioMarkerAsync(fileId, markerId, request, token);

    public Task<IReadOnlyList<AudioMarkerRecord>> ScanAudioForEvpAsync(Guid fileId, EvpSensitivity sensitivity, EvpDetectionOptions? options = null, CancellationToken token = default)
        => _api.ScanAudioForEvpAsync(fileId, sensitivity, options, token);

    // ── Audio Clip ────────────────────────────────────────────────────────────

    public Task<UploadFileRecord?> ClipAudioAsync(Guid fileId, ClipAudioRequest request, CancellationToken token = default)
        => _api.ClipAudioAsync(fileId, request, token);

    public Task<IReadOnlyList<UploadFileRecord>> GetChildClipsAsync(Guid fileId, CancellationToken token = default)
        => _api.GetChildClipsAsync(fileId, token);

    public Task<UploadFileRecord?> EditAudioAsync(Guid fileId, AudioEditRequest request, CancellationToken token = default)
        => _api.EditAudioAsync(fileId, request, token);

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

    // ── Video projects ────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<VideoProjectRecord>> GetMyVideoProjectsAsync(Guid? caseId = null, CancellationToken token = default)
    {
        var url = caseId.HasValue ? $"/api/video-projects?caseId={caseId}" : "/api/video-projects";
        var result = await _api.GetAsync<IReadOnlyList<VideoProjectRecord>>(url, token);
        return result ?? [];
    }
    public Task<VideoProjectRecord?> GetMyVideoProjectAsync(Guid id, CancellationToken token = default)
        => _api.GetAsync<VideoProjectRecord>($"/api/video-projects/{id}", token);
    public Task<VideoProjectRecord?> SaveMyVideoProjectAsync(Ben.Video.Editor.Models.ProjectFile file, Guid? caseId = null, CancellationToken token = default)
    {
        var url = caseId.HasValue ? $"/api/video-projects?caseId={caseId}" : "/api/video-projects";
        return _api.PostAsync<Ben.Video.Editor.Models.ProjectFile, VideoProjectRecord>(url, file, token);
    }
    public Task<VideoProjectRecord?> UpdateMyVideoProjectAsync(Guid id, Ben.Video.Editor.Models.ProjectFile file, CancellationToken token = default)
        => _api.PutAsync<Ben.Video.Editor.Models.ProjectFile, VideoProjectRecord>($"/api/video-projects/{id}", file, token);
    public Task<VideoProjectRecord?> PublishVideoProjectAsync(Guid id, byte[] bytes, string fileName, string contentType, CancellationToken token = default)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        var form = new MultipartFormDataContent();
        form.Add(fileContent, "file", fileName);
        return _api.PostMultipartAsync<VideoProjectRecord>($"/api/video-projects/{id}/publish", form, token);
    }
    public Task<bool> DeleteMyVideoProjectAsync(Guid id, CancellationToken token = default)
        => _api.DeleteAsync($"/api/video-projects/{id}", token);

    // ── Image editor ────────────────────────────────────────────────────────
    public Task<UploadFileRecord?> SaveImageEditStateAsync(Guid fileId, string? editStateJson, CancellationToken token = default)
        => _api.PutAsync<object, UploadFileRecord>($"/api/upload-files/{fileId}/edit-state", new { EditStateJson = editStateJson }, token);
    public Task<UploadFileRecord?> SaveImageAsNewVersionAsync(Guid parentFileId, byte[] imageBytes, string format, CancellationToken token = default)
    {
        var mime = format == "jpeg" ? "image/jpeg" : "image/png";
        var ext  = format == "jpeg" ? ".jpg" : ".png";
        var content = new ByteArrayContent(imageBytes);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mime);
        var form = new MultipartFormDataContent();
        form.Add(content, "file", $"edited{ext}");
        return _api.PostMultipartAsync<UploadFileRecord>($"/api/upload-files/{parentFileId}/save-as-version", form, token);
    }
}
