namespace Ben.Service.Models.Entities;

/// <summary>The group's own view of its invitation link.</summary>
public record OrganizationJoinLinkRecord
{
    public Guid OrganizationId { get; init; }

    /// <summary>The secret in the link, or null when the group has no live invitation.</summary>
    public string? Token { get; init; }

    /// <summary>When it stops working. Null when there is no link.</summary>
    public DateTime? ExpiresUtc { get; init; }

    /// <summary>
    /// What the plan will say to the next person who uses this link, or null when it will let
    /// them in.
    /// </summary>
    /// <remarks>
    /// Carried beside the link so the screen can say it BEFORE the link is shared. The refusal is
    /// knowable in advance, and item 193's rule is that a knowable refusal is said beside the
    /// control rather than after somebody has already pasted the link into a group chat.
    /// </remarks>
    public string? PlanRefusal { get; init; }
}

/// <summary>What somebody holding an invitation link is told before they accept it.</summary>
public record OrganizationJoinInviteRecord
{
    public Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public string? OrganizationUrlName { get; init; }
    public DateTime? ExpiresUtc { get; init; }

    /// <summary>They are already in this group — the page says so rather than offering to add them twice.</summary>
    public bool AlreadyAMember { get; init; }
}
