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

/// <summary>
/// The numbers the navigation bar shows, counted by the database (10/14/2026).
/// </summary>
/// <remarks>
/// Ben: "Is there any way we speed up the logic for getting counts … so it doesn't get slower for
/// site users the more users we have." The bar used to download every user (for a SuperAdmin's
/// badge) and every group with its member, case and investigation counts and permissions, only to
/// count the rows — a page load that grew with the site. Each figure here is one SQL COUNT or
/// EXISTS over indexed columns.
/// </remarks>
/// <param name="OrganizationCount">Groups this person sees: all of them for a SuperAdmin, their active memberships otherwise.</param>
/// <param name="HasStoreOrders">Whether they have any paid store order.</param>
/// <param name="UserCount">Every account on the site; SuperAdmins only, null for anybody else.</param>
public sealed record NavCountsResponse(int OrganizationCount, bool HasStoreOrders, int? UserCount);
