using Ben.Data.Common.Enums;

namespace Ben.Web.WebApp.Services.WebApi;

public sealed class OrganizationSummaryResponse
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UrlName { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; }
    public Guid CreatedByAppUserId { get; set; }
}

public sealed class UserSearchResultResponse
{
    public Guid AppUserId { get; set; }
    public string? DisplayName { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }

    public string DisplayLabel =>
        !string.IsNullOrWhiteSpace(DisplayName)
            ? $"{DisplayName} ({Email ?? UserName ?? AppUserId.ToString()})"
            : (Email ?? UserName ?? AppUserId.ToString());
}

public sealed class RegisterOrganizationRequest
{
    public string Name { get; set; } = string.Empty;
    public string UrlName { get; set; } = string.Empty;
}

public sealed class CheckOrganizationAccessRequest
{
    public Guid AppUserId { get; set; }
    public OrganizationSecurityTable Table { get; set; }
    public OrganizationSecurityAction Action { get; set; }
}

public sealed class UpsertOrganizationMembershipRequest
{
    public OrganizationMemberRole Role { get; set; } = OrganizationMemberRole.Member;
    public bool IsActive { get; set; } = true;
}

public sealed class SetOrganizationGrantRequest
{
    public OrganizationSecurityTable Table { get; set; }
    public OrganizationSecurityAction Actions { get; set; }
}

public sealed class OrganizationUserMembershipResponse
{
    public Guid MembershipId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AppUserId { get; set; }
    public string? DisplayName { get; set; }
    public OrganizationMemberRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
}

public sealed class OrganizationAccessGrantResponse
{
    public Guid GrantId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AppUserId { get; set; }
    public OrganizationSecurityTable Table { get; set; }
    public OrganizationSecurityAction Actions { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
}