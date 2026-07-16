namespace Ben.Service.Models.Admin;

public record AppUserAdminRecord
{
    public Guid Id { get; init; }
    public string? UserName { get; init; }
    public string? NormalizedUserName { get; init; }
    public string? Email { get; init; }
    public string? NormalizedEmail { get; init; }
    public bool IsEmailConfirmed { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsPhoneNumberConfirmed { get; init; }
    public bool IsTwoFactorEnabled { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public bool IsLockoutEnabled { get; init; }
    public int AccessFailedCount { get; init; }
    public string? DisplayName { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
}
