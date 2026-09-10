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

/// <summary>Asking what the guest mail would look like, from what is in the editor right now.</summary>
public sealed record TourMailPreviewRequest(string? SubjectTemplate, string? BodyTemplate);

/// <summary>The mail as it would go out.</summary>
/// <param name="CalendarFile">
/// The .ics text, so a business can see what lands in a guest's diary rather than taking it on
/// trust.
/// </param>
/// <param name="UsesARealDate">
/// True when the preview was rendered against a date actually on the calendar; false means the
/// times shown are a sample.
/// </param>
public sealed record TourMailPreviewRecord(
    string Subject, string HtmlBody, string CalendarFile, bool UsesARealDate);

/// <summary>
/// The placeholders a tour business may use in its guest email (item 233).
/// </summary>
/// <remarks>
/// Shared, because two things need it: the renderer that substitutes them, and the editor that
/// lists them for somebody writing the mail. A second copy would drift within a month, and the
/// symptom would be a business writing a placeholder that renders as nothing.
/// </remarks>
public static class TourMailTokens
{
    public static IReadOnlyList<(string Token, string Means)> All { get; } =
    [
        ("{{tour.name}}",           "The tour's name"),
        ("{{tour.description}}",    "What you wrote about the tour"),
        ("{{tour.meetingPoint}}",   "Where it starts, in full"),
        ("{{tour.meetingPointMap}}","A link that opens the meeting point in Maps"),
        ("{{tour.length}}",         "How long it runs — \"about 1 hour 30 minutes\""),
        ("{{date.start}}",          "When this one starts, in your tour's time zone"),
        ("{{date.end}}",            "When it is expected to finish"),
        ("{{date.day}}",            "The day it runs — \"Saturday, 09/13/2026\""),
        ("{{date.capacity}}",       "How many people this date takes"),
        ("{{date.spacesLeft}}",     "How many places are still open"),
        ("{{date.title}}",          "What the date is called on your calendar"),
        ("{{date.url}}",            "A link to the date's page on this site"),
        ("{{guide.names}}",         "Who is leading this one"),
        ("{{guide.photos}}",        "Their photographs, where they have published one"),
        ("{{guide.block}}",         "\"Your guide: Gale\" with their photograph — nothing when no guide is set"),
        ("{{guest.name}}",          "Who you are writing to"),
        ("{{business.name}}",       "Your business"),
        ("{{business.url}}",        "A link to your public page"),
        ("{{business.contact}}",    "Your contact line — how to reach you and how to pay"),
        ("{{business.contactBlock}}","The same, as its own paragraph — nothing when you have not written one"),
        ("{{site.name}}",           "IsHaunted.com"),
    ];
}
