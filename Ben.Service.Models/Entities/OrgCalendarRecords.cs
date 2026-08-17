using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record OrgCalendarEventTypeRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public string? ColorClass { get; init; }
    public string? IconClass { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

public record OrgCalendarEventRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? EventTypeId { get; init; }
    public string? EventTypeName { get; init; }
    public string? EventTypeColor { get; init; }
    public Guid? CaseId { get; init; }
    public string? CaseReference { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }

    /// <summary>One of the org's saved addresses, when the event is held at one.</summary>
    public Guid? OrganizationAddressId { get; init; }

    /// <summary>The saved address rendered for display, so callers need no second lookup.</summary>
    public string? OrganizationAddressLabel { get; init; }

    /// <summary>Video call link — Zoom, Teams, or similar.</summary>
    public string? MeetingUrl { get; init; }

    public DateTime StartDateTime { get; init; }
    public DateTime EndDateTime { get; init; }
    public bool IsAllDay { get; init; }
    public bool IsPublic { get; init; }

    // ── Public-event settings (item #87) ────────────────────────────────────

    /// <summary>The shared place this event is at — its map pin, and how we know it is not a home.</summary>
    public Guid? PlaceId { get; init; }

    /// <summary>Show the town but not the street until somebody says they are coming.</summary>
    public bool HideExactLocation { get; init; }

    /// <summary>How many may say they are coming, or null for no limit.</summary>
    public int? AttendeeCapacity { get; init; }

    /// <summary>After this, no new attendees. Null means right up to the start.</summary>
    public DateTime? RsvpClosesAt { get; init; }

    /// <summary>The readable slug this event is public at, or null while it is private.</summary>
    public string? UrlName { get; init; }

    public string? RecurrenceRule { get; init; }
    public int AttendeeCount { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}

public record OrgCalendarEventAttendeeRecord
{
    public Guid Id { get; init; }
    public Guid OrgCalendarEventId { get; init; }
    public Guid AppUserId { get; init; }
    public string? DisplayName { get; init; }
    public RsvpStatus RsvpStatus { get; init; }
    public string? AssignedTask { get; init; }
    public DateTime? DateRsvp { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
