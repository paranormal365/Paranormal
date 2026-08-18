using Ben.Data.Common.Enums;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Web.Services;
using Microsoft.Extensions.Options;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The User half of the adapter — implements <see cref="Ben.Web.Services.IBenUserClient"/>.
/// </summary>
/// <remarks>
/// One partial class split across files by domain, matching the slices of IBenAdminClient.
/// The constructor and shared fields live in BenAdminClientAdapter.cs.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
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
}
