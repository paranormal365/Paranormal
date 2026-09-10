using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// A tour as its business sees it (item 233).
/// </summary>
/// <remarks>
/// The tour is the product the business pays for — <b>Ben, 2026-09-10</b>: "The $29 per month is
/// for a single tour no matter how many times scheduled" — so this record carries what the
/// business needs to run and price it, and <see cref="PlanNote"/> tells them in a sentence what
/// creating it did to the bill.
/// </remarks>
public sealed record TourRecord(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? Description,
    Guid StartOrganizationAddressId,
    string StartAddressLabel,
    decimal? StartLatitude,
    decimal? StartLongitude,
    int? DurationMinutes,
    int? DefaultCapacity,
    string TimeZoneId,
    bool AllowReviews,
    bool IsBookable,
    string? ContactLine,
    string? MailSubjectTemplate,
    string? MailBodyTemplate,
    DateTime? RetiredAtUtc,
    IReadOnlyList<TourGuideRecord> Guides,
    int UpcomingDateCount,
    int TotalDateCount,
    DateTime? NextDateStartUtc,
    string? PlanNote = null)
{
    public bool IsActive => RetiredAtUtc is null;
}

/// <summary>One of a tour's guides — the person a guest will meet.</summary>
/// <param name="PhotoUploadFileId">
/// Their public profile photograph, when they have one. Optional by design: Ben asked for it "for
/// safety", not as a requirement, and a guide who has published no photograph is still a guide.
/// </param>
public sealed record TourGuideRecord(
    Guid AppUserId,
    string DisplayName,
    string? Handle,
    Guid? PhotoUploadFileId);

/// <summary>Creating or changing a tour.</summary>
public sealed record UpsertTourRequest(
    string Name,
    string? Description,
    Guid StartOrganizationAddressId,
    int? DurationMinutes,
    int? DefaultCapacity,
    string? TimeZoneId,
    bool AllowReviews = true,
    bool IsBookable = true,
    string? ContactLine = null,
    string? MailSubjectTemplate = null,
    string? MailBodyTemplate = null);

/// <summary>Who guides a tour, replacing the whole list.</summary>
public sealed record SetTourGuidesRequest(IReadOnlyList<Guid> AppUserIds);

/// <summary>
/// What the plan says about tours right now, so a business knows the price before it clicks.
/// </summary>
/// <param name="CoveredTours">Tours the current period was already paid for.</param>
/// <param name="ActiveTours">Tours the business runs today.</param>
/// <param name="NextTourCostsToday">
/// What one more tour would be charged now for the rest of this period. Null when the business
/// has no paid period running, in which case a tour costs nothing until it subscribes.
/// </param>
public sealed record TourPlanRecord(
    bool IsPerTour,
    string TierName,
    decimal UnitPrice,
    BillingInterval Interval,
    int CoveredTours,
    int ActiveTours,
    decimal? NextTourCostsToday,
    DateTime? CurrentPeriodEndUtc,
    bool HasCardOnFile);

/// <summary>One picture on a tour's page.</summary>
/// <param name="FromAGuest">
/// True when it was kept from something a guest sent in, so the business can tell its own
/// photographs from the ones it was given.
/// </param>
public sealed record TourImageRecord(
    Guid Id, Guid UploadFileId, int SortOrder, string? Caption, bool FromAGuest);

/// <summary>Changing a picture's caption or where it sits.</summary>
public sealed record UpdateTourImageRequest(string? Caption, int? SortOrder);
