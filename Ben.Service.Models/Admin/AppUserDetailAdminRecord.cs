namespace Ben.Service.Models.Admin;

/// <summary>
/// Full user aggregate returned by GET /api/admin/app-users/{id}/detail.
/// Includes the user profile and all related records for SuperAdmin inspection.
/// </summary>
public record AppUserDetailAdminRecord
{
    public required AppUserAdminRecord User { get; init; }
    public IReadOnlyList<UserAddressAdminRecord> Addresses { get; init; } = [];
    public IReadOnlyList<UserEmailAdminRecord> Emails { get; init; } = [];
    public IReadOnlyList<UserPhoneAdminRecord> Phones { get; init; } = [];
    public IReadOnlyList<UserLinkAdminRecord> Links { get; init; } = [];
    public IReadOnlyList<UserNoteAdminRecord> Notes { get; init; } = [];
    public IReadOnlyList<UserMessageAdminRecord> Messages { get; init; } = [];
    public IReadOnlyList<OrganizationUserMembershipAdminRecord> Memberships { get; init; } = [];
    public IReadOnlyList<UploadFileAdminRecord> UploadFiles { get; init; } = [];
}
