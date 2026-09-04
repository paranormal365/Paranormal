namespace Ben.Service.Models.Entities;

/// <summary>
/// A member's desk: the work waiting for them, in one call (item 204).
/// </summary>
/// <remarks>
/// The "has work waiting" banners already knew most of this; the Home page beneath them repeated
/// the visitor hero. Everything here is a fact the site already serves somewhere — this only puts
/// it on one screen. Counts are honest totals; the lists are the first few, newest or soonest.
/// </remarks>
public sealed record MemberDeskResponse(
    /// <summary>Groups this person is an active member of. Zero means the desk has nothing to stand on.</summary>
    int GroupCount,
    DeskInvestigation? NextInvestigation,
    int UpcomingInvestigationCount,
    int OpenCaseCount,
    IReadOnlyList<DeskCase> OpenCases,
    int UnreadMessageCount,
    int GearCheckedOutCount,
    IReadOnlyList<DeskGear> GearCheckedOut,
    int OverdueGearCount,
    /// <summary>Client requests and membership applications waiting on groups this person can act for.</summary>
    int PendingRequestCount);

public sealed record DeskInvestigation(
    Guid Id, string Title, string? UrlName, DateTime ScheduledDateTime, DateTime? EndDateTime,
    Guid OrganizationId, string OrganizationName, string OrganizationUrlName,
    string? LocationLabel, bool IsLead, int AttendeeCount);

public sealed record DeskCase(
    Guid Id, string Title, string? UrlName, int CaseYear, int OrgCaseNumber, string Status,
    Guid OrganizationId, string OrganizationName, string OrganizationUrlName,
    DateTime DateCaseOpened, bool IsContact);

public sealed record DeskGear(
    Guid CheckoutId, Guid EquipmentItemId, string DisplayName, string? OrganizationName,
    DateTime? DateCheckedOut, DateTime? DateDue, bool IsOverdue);
