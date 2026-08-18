using Ben.Service.Models.Admin;
using Ben.Service.Models.Support;
using Ben.Service.Models.Entities;
using Ben.Service.Models.People;
using Ben.Data.Common.Enums;

namespace Ben.Web.Services;

/// <summary>
/// The User slice of <see cref="IBenAdminClient"/> — people — profiles, contact details, accounts and impersonation.
/// </summary>
/// <remarks>
/// Part of splitting one 383-method interface into domain-sized pieces.
/// <see cref="IBenAdminClient"/> inherits every slice, so existing callers and the single
/// adapter are unchanged; new code (and test doubles) can depend on just the slice it needs.
/// </remarks>
public interface IBenUserClient
{
    // ── My profile (Area 4 / U1) ─────────────────────────────────────────────
    // Everything here is implicitly scoped to the signed-in user; none of these take a user id.

    /// <summary>The current user's own profile, including their active public/private photos.</summary>
    Task<MyProfileRecord?> GetMyProfileAsync(CancellationToken token = default);

    /// <summary>
    /// Updates the current user's profile. A null <c>DisplayName</c> leaves the existing name
    /// untouched; an empty or whitespace one clears it.
    /// </summary>
    Task<MyProfileRecord?> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken token = default);

    /// <summary>Every photo the current user has set, newest first — including inactive ones.</summary>
    Task<List<AppUserPhotoRecord>> GetMyPhotosAsync(CancellationToken token = default);

    /// <summary>Makes an already-uploaded file the current user's photo for one slot.</summary>
    Task<AppUserPhotoRecord?> SetMyPhotoAsync(SetMyPhotoRequest request, CancellationToken token = default);

    /// <summary>Removes one of the current user's photos. The underlying file is kept.</summary>
    Task<bool> DeleteMyPhotoAsync(Guid photoId, CancellationToken token = default);

    /// <summary>The UploadFileType new profile-photo uploads belong to.</summary>
    Task<Guid?> GetProfilePhotoFileTypeIdAsync(CancellationToken token = default);

    /// <summary>
    /// Another user's profile photo, already resolved to whichever one the caller may see.
    /// Null when they have no visible photo — render initials rather than a broken image.
    /// </summary>
    Task<(byte[] Data, string ContentType)?> GetUserAvatarAsync(Guid userId, CancellationToken token = default);

    // ── My contact info (self-service emails/phones/addresses/links) ─────────
    // Same scoping rule as My profile above: every call is implicitly the signed-in user, so none
    // of these take a user id — there is no "edit someone else" shape to get wrong.

    Task<List<MyEmailRecord>> GetMyEmailsAsync(CancellationToken token = default);
    Task<MyEmailRecord?> CreateMyEmailAsync(UpsertMyEmailRequest request, CancellationToken token = default);
    Task<MyEmailRecord?> UpdateMyEmailAsync(Guid id, UpsertMyEmailRequest request, CancellationToken token = default);
    Task<bool> DeleteMyEmailAsync(Guid id, CancellationToken token = default);

    /// <summary>
    /// Issues (or reissues) a validation link for one of the caller's emails. The link is always
    /// returned, whether or not it could also be emailed — see <see cref="SendValidationResponse"/>.
    /// </summary>
    Task<SendValidationResponse?> SendMyEmailValidationAsync(Guid id, CancellationToken token = default);

    Task<List<MyPhoneRecord>> GetMyPhonesAsync(CancellationToken token = default);
    Task<MyPhoneRecord?> CreateMyPhoneAsync(UpsertMyPhoneRequest request, CancellationToken token = default);
    Task<MyPhoneRecord?> UpdateMyPhoneAsync(Guid id, UpsertMyPhoneRequest request, CancellationToken token = default);
    Task<bool> DeleteMyPhoneAsync(Guid id, CancellationToken token = default);

    Task<List<MyAddressRecord>> GetMyAddressesAsync(CancellationToken token = default);
    Task<MyAddressRecord?> CreateMyAddressAsync(UpsertMyAddressRequest request, CancellationToken token = default);
    Task<MyAddressRecord?> UpdateMyAddressAsync(Guid id, UpsertMyAddressRequest request, CancellationToken token = default);
    Task<bool> DeleteMyAddressAsync(Guid id, CancellationToken token = default);

    Task<List<MyLinkRecord>> GetMyLinksAsync(CancellationToken token = default);
    Task<MyLinkRecord?> CreateMyLinkAsync(UpsertMyLinkRequest request, CancellationToken token = default);
    Task<MyLinkRecord?> UpdateMyLinkAsync(Guid id, UpsertMyLinkRequest request, CancellationToken token = default);
    Task<bool> DeleteMyLinkAsync(Guid id, CancellationToken token = default);

    // ── Email validation redemption (anonymous — the confirming visitor may have no session) ──

    /// <summary>Info for the validation landing page: masked address + whether the link is still good.</summary>
    Task<EmailValidationInfoRecord?> GetEmailValidationInfoAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Confirms the address the token was issued for. True on success.</summary>
    Task<bool> ConfirmEmailValidationAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Approves or denies a file-permission request. Returns false when the API rejects it.</summary>
    Task<bool> ReviewPermissionRequestAsync(
        Guid requestId, FilePermissionRequestStatus status, string? reviewNotes, CancellationToken token = default);

    // ── Users ─────────────────────────────────────────────────────────────────

    /// <summary>Returns a lightweight list of all application users. SuperAdmin only — see
    /// EntityReadControllerBase's doc comment. Org-admin surfaces that only need to resolve
    /// member display names should use <see cref="GetOrgUserDirectoryAsync"/> instead.</summary>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    Task<IReadOnlyList<AppUserRecord>> GetAllUsersAsync(CancellationToken token = default);

    /// <summary>Returns a minimal Id+DisplayName directory of one organization's active
    /// members — for org-admin surfaces (e.g. CMS permission/member pickers) that only need to
    /// resolve names, not the full <see cref="AppUserRecord"/>. Caller must be an active member
    /// of <paramref name="organizationId"/> themselves.</summary>
    /// <param name="organizationId">The organization whose member directory to return.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    Task<IReadOnlyList<OrgUserDirectoryItem>> GetOrgUserDirectoryAsync(Guid organizationId, CancellationToken token = default);

    /// <summary>Returns the full detail aggregate for a single user, including addresses, emails, phones, links, notes, memberships, and files.</summary>
    /// <param name="userId">The <see cref="Guid"/> primary key of the user.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The detail record, or <c>null</c> if the user was not found.</returns>
    Task<AppUserDetailAdminRecord?> GetUserDetailAsync(Guid userId, CancellationToken token = default);

    /// <summary>Creates a new application user with an initial password.</summary>
    /// <param name="request">The new user fields including email, password, display name and role flags.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The created <see cref="AppUserAdminRecord"/>, or <c>null</c> if creation failed.</returns>
    Task<AppUserAdminRecord?> CreateUserAsync(AdminCreateUserRequest request, CancellationToken token = default);

    /// <summary>Updates editable profile fields for a user including audit timestamps.</summary>
    /// <param name="userId">The <see cref="Guid"/> primary key of the user to update.</param>
    /// <param name="request">The updated profile values.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns>The updated admin record, or <c>null</c> if the update failed.</returns>
    Task<AppUserAdminRecord?> UpdateUserProfileAsync(Guid userId, AdminUpdateUserProfileRequest request, CancellationToken token = default);

    // ── Impersonation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Begins an impersonation session as <paramref name="targetUserId"/>.
    /// Saves the current SuperAdmin token and replaces it with a token issued for the target user.
    /// </summary>
    /// <param name="targetUserId">The user to impersonate.</param>
    /// <param name="targetUserEmail">Display email stored alongside the impersonation token.</param>
    /// <param name="token">Propagates cancellation from the Blazor component.</param>
    /// <returns><c>true</c> if impersonation succeeded; <c>false</c> otherwise.</returns>
    Task<bool> ImpersonateUserAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default);

    /// <summary>
    /// Ends the active impersonation session and restores the original SuperAdmin token.
    /// </summary>
    /// <remarks>
    /// Calls <c>/api/me</c> to re-establish IsSuperAdmin on the restored token — the Identity
    /// API's opaque tokens can't have that claim read back out of them locally.
    /// </remarks>
    Task StopImpersonatingAsync(CancellationToken token = default);

    // ── User sub-entity type lists (for dropdowns) ────────────────────────────

    Task<IReadOnlyList<UserAddressTypeRecord>> GetUserAddressTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserEmailTypeRecord>> GetUserEmailTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserPhoneTypeRecord>> GetUserPhoneTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserLinkTypeRecord>> GetUserLinkTypesAsync(CancellationToken token = default);
    Task<IReadOnlyList<UserNoteTypeRecord>> GetUserNoteTypesAsync(CancellationToken token = default);

    // Type management (SuperAdmin creates new types)
    Task<bool> CreateUserAddressTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserEmailTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserPhoneTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserLinkTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);
    Task<bool> CreateUserNoteTypeAsync(string name, string? description = null, bool isActive = true, bool isPublic = false, int sortOrder = 0, string? iconClass = null, string? colorClass = null, CancellationToken token = default);

    // ── User sub-entity CRUD (SuperAdmin) ─────────────────────────────────────

    Task<bool> CreateUserAddressAsync(Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserAddressAsync(Guid id, Guid userId, Guid actorId, UserAddressUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserAddressAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserEmailAsync(Guid userId, Guid actorId, UserEmailUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserEmailAsync(Guid id, Guid userId, Guid actorId, UserEmailUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserEmailAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserPhoneAsync(Guid userId, Guid actorId, UserPhoneUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserPhoneAsync(Guid id, Guid userId, Guid actorId, UserPhoneUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserPhoneAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserLinkAsync(Guid userId, Guid actorId, UserLinkUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserLinkAsync(Guid id, Guid userId, Guid actorId, UserLinkUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserLinkAsync(Guid id, CancellationToken token = default);

    Task<bool> CreateUserNoteAsync(Guid userId, Guid actorId, UserNoteUpsertRequest req, CancellationToken token = default);
    Task<bool> UpdateUserNoteAsync(Guid id, Guid userId, Guid actorId, UserNoteUpsertRequest req, CancellationToken token = default);
    Task<bool> DeleteUserNoteAsync(Guid id, CancellationToken token = default);

    Task<bool> DeleteUploadFileAdminAsync(Guid id, CancellationToken token = default);
}
