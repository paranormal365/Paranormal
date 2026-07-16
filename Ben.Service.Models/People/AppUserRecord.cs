namespace Ben.Service.Models.People;

public record AppUserRecord
{
    public Guid Id { get; init; }
    public string? UserName { get; init; }
    public string? DisplayName { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public string? Email { get; init; }
    public bool IsEmailConfirmed { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsPhoneNumberConfirmed { get; init; }
    public bool IsTwoFactorEnabled { get; init; }
}
