namespace Ben.Service.Models.Entities;

/// <summary>One date of a hosted event, as a screen sees it (item 235).</summary>
/// <param name="Label">
/// What to call it — the host's own title, or "Friday 30 October" when they have not given one.
/// Built on the server so the web and the phone cannot disagree about a date's name.
/// </param>
/// <param name="StartsLocal">When it starts, in the event's own zone. Null = the event's default.</param>
public sealed record HostedEventNightRecord(
    Guid Id,
    DateTime Date,
    string Label,
    string? Title,
    TimeSpan? StartsLocal,
    TimeSpan? EndsLocal,
    string? Notes,
    int SortOrder);

/// <summary>
/// What a hosted event costs this organization, and whether another may go live.
/// </summary>
/// <param name="PaysWith">"plan" or "credit" — which of the two answers applies.</param>
/// <param name="Refusal">Why publishing is refused, or null when it may go ahead.</param>
/// <param name="Sentence">
/// What the page says before anybody presses anything: what publishing will do and what it costs.
/// </param>
/// <remarks>
/// Read by the event list and by the publish confirmation, so the answer somebody is warned about
/// and the answer they get are computed once, in the same place.
/// </remarks>
public sealed record HostedEventPlanRecord(
    string PaysWith,
    string? Refusal,
    string Sentence,
    int LiveNow,
    int? Ceiling,
    int CreditsAvailable,
    bool MayPublish,
    bool SpendsACredit);

/// <summary>A hosted event as its organizers see it (item 235).</summary>
/// <param name="DatesAreSeparate">
/// True when each date stands on its own — a run of performances rather than one stay. The event is
/// then the production and each date a performance of it.
/// </param>
/// <param name="DateNoun">
/// "night" or "date", singular, so every screen says the same word for the same thing without
/// nine of them each deciding.
/// </param>
/// <param name="PlanNote">What the last action did to the plan, when it did anything.</param>
public sealed record HostedEventRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Name,
    string UrlName,
    string? Tagline,
    string? Description,
    Guid PlaceId,
    string PlaceLabel,
    bool HideExactLocation,
    string TimeZoneId,
    DateTime StartsOn,
    DateTime EndsOn,
    bool DatesAreSeparate,
    string DateNoun,
    TimeSpan? DefaultStartLocal,
    TimeSpan? DefaultEndLocal,
    bool IsPublished,
    DateTime? FirstPublishedUtc,
    int? DayPassCapacity,
    string? ContactLine,
    Guid? CoverUploadFileId,
    string? MailSubjectTemplate,
    string? MailBodyTemplate,
    bool CollectsEvidence,
    DateTime? ArchivedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancelledReason,
    Guid? UmbrellaEventId,
    IReadOnlyList<HostedEventNightRecord> Nights,
    string? PlanNote = null);

/// <summary>
/// A venue being entered as part of the event that happens there (item 235).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-11:</b> "They may need to enter the name and address and information about
/// a new venue we have not listed before." Which is how most of them will start: a hotel, a theatre
/// or a hall the site has never heard of, entered by the person who is about to run an event in
/// it.</para>
///
/// <para>Shaped exactly like the inline place an investigation creates, and resolved through the
/// same matcher, so entering "The Thomas House Hotel" twice finds the one that already exists
/// rather than making a second. A venue is a shared place and always has been.</para>
///
/// <para><b>Public by default, and only ever public.</b> Every other inline place on this site
/// defaults to a private residence, which is the cautious answer where somebody's home is the
/// likely subject. Here the cautious answer is the opposite: an event is published by definition,
/// and a private residence is refused outright, so a venue that arrives with no kind is a venue.</para>
/// </remarks>
public sealed record NewVenueRequest(
    string? Name,
    string? StreetAddress1 = null,
    string? StreetAddress2 = null,
    string? City = null,
    string? State = null,
    string? ZipCode = null,
    string? Country = null,
    decimal? Latitude = null,
    decimal? Longitude = null)
{
    /// <summary>Whether anything at all was entered, rather than an empty form.</summary>
    public bool HasAnything =>
        !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(StreetAddress1)
        || !string.IsNullOrWhiteSpace(City);
}

/// <summary>Creating or changing a hosted event.</summary>
/// <remarks>
/// Publishing is not here. It is its own endpoint because it is the only thing on this screen that
/// costs money, and a field on a save form is not a place to spend somebody's $99.
/// </remarks>
public sealed record UpsertHostedEventRequest(
    string Name,
    /// <summary>
    /// An existing venue. Empty when <see cref="NewVenue"/> carries one that is not on the site yet.
    /// </summary>
    Guid PlaceId,
    DateTime StartsOn,
    DateTime EndsOn,
    string? TimeZoneId = null,
    string? Tagline = null,
    string? Description = null,
    bool HideExactLocation = false,
    bool DatesAreSeparate = false,
    TimeSpan? DefaultStartLocal = null,
    TimeSpan? DefaultEndLocal = null,
    int? DayPassCapacity = null,
    string? ContactLine = null,
    Guid? CoverUploadFileId = null,
    string? MailSubjectTemplate = null,
    string? MailBodyTemplate = null,
    bool CollectsEvidence = false,
    /// <summary>
    /// The dates of a run, chosen one at a time. Ignored for a stay, whose dates are every day
    /// between its ends.
    /// </summary>
    IReadOnlyList<DateTime>? Dates = null,
    /// <summary>
    /// A venue the site has never listed, entered here rather than somewhere else first.
    /// </summary>
    NewVenueRequest? NewVenue = null);

/// <summary>Changing one date of an event.</summary>
public sealed record UpsertHostedEventNightRequest(
    string? Title = null,
    TimeSpan? StartsLocal = null,
    TimeSpan? EndsLocal = null,
    string? Notes = null);

/// <summary>Calling an event off, with the reason the people who have places will read.</summary>
public sealed record CancelHostedEventRequest(string? Reason = null);

/// <summary>A hosted event as a visitor sees it (item 235).</summary>
/// <remarks>
/// Published events only. The exact address is withheld exactly as a public calendar event withholds
/// it, so a reader without a place sees the town and no more when the host asked for that.
/// </remarks>
public sealed record PublicHostedEventRecord(
    Guid Id,
    Guid UmbrellaEventId,
    string OrganizationName,
    string OrganizationUrlName,
    string Name,
    string UrlName,
    string? Tagline,
    string? Description,
    string TimeZoneId,
    DateTime StartsOn,
    DateTime EndsOn,
    bool DatesAreSeparate,
    string DateNoun,
    string? VenueName,
    string? City,
    string? State,
    string? ExactAddress,
    bool IsExactAddressHidden,
    decimal? Latitude,
    decimal? Longitude,
    int? DayPassCapacity,
    string? ContactLine,
    Guid? CoverUploadFileId,
    bool IsCancelled,
    string? CancelledReason,
    IReadOnlyList<HostedEventNightRecord> Nights);
