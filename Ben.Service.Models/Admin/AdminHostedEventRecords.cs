using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Admin;

// ── the SuperAdmin's view of hosted events (item 235 phase 17b) ───────────────

/// <summary>One hosted event on the SuperAdmin's list.</summary>
/// <param name="CreatedByName">The person who created the event, as the organizer the list names.</param>
/// <param name="ConfirmedPeople">People with a confirmed place.</param>
/// <param name="WaitingParties">Parties asking or holding, not yet answered.</param>
/// <param name="AppealState">The newest removal's appeal, when the event has been removed.</param>
public sealed record AdminHostedEventRow(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationUrlName,
    string EventName,
    string UrlName,
    Guid CreatedByAppUserId,
    string? CreatedByName,
    DateTime StartsOn,
    DateTime EndsOn,
    HostedEventLifecycleState State,
    int ConfirmedPeople,
    int WaitingParties,
    string? VenueName,
    string? City,
    string? Region,
    DateTime DateCreated,
    HostedEventAppealState? AppealState);

/// <summary>An organizer's appeal against a removal, as the reviewer reads it.</summary>
/// <param name="RemovalNote">What the SuperAdmin who removed it noted. Never sent to the organizer.</param>
public sealed record AdminHostedEventAppealRecord(
    Guid RemovalId,
    Guid HostedEventId,
    Guid OrganizationId,
    string OrganizationName,
    string EventName,
    DateTime StartsOn,
    HostedEventLifecycleState PreviousState,
    string? RemovalNote,
    string RemovedByName,
    DateTime RemovedUtc,
    bool CreditReturned,
    HostedEventAppealState AppealState,
    string? AppealMessage,
    string? AppealedByName,
    DateTime? AppealedUtc,
    string? DecisionNote,
    string? DecidedByName,
    DateTime? DecidedUtc);

/// <summary>Every hosted event, the appeals waiting and recently answered, and a note about what was just done.</summary>
public sealed record AdminHostedEventsRecord(
    IReadOnlyList<AdminHostedEventRow> Events,
    IReadOnlyList<AdminHostedEventAppealRecord> Appeals,
    string? Note = null);

/// <summary>What removing an event would do, shown before the SuperAdmin confirms.</summary>
public sealed record AdminHostedEventRemovalEffect(
    bool AlreadyRemoved,
    bool CreditReturns,
    int PartiesWithPlaces,
    int PeopleWithPlaces);

/// <summary>Removes an event. The note is for the site's own people and is never sent.</summary>
public sealed record RemoveHostedEventRequest(string? Note);

/// <summary>Answers an appeal. A declined appeal needs a note; the organizer reads it.</summary>
public sealed record DecideHostedEventAppealRequest(bool Uphold, string? Note);

/// <summary>The events dashboard: where every hosted event stands, and what happened in the chosen window.</summary>
/// <param name="OnTheSite">Published or on now.</param>
/// <param name="Happened">Ended or archived.</param>
/// <param name="CalledOff">Cancelled, withdrawn by the venue, or removed.</param>
/// <param name="Organizers">Groups with at least one hosted event.</param>
/// <param name="Venues">Published venue profiles.</param>
/// <param name="VerifiedVenues">Of those, confirmed as the venue for their building.</param>
/// <param name="CreditsBought">Credits bought (not granted) in the window.</param>
/// <param name="CreditsSpent">Credits spent on an event in the window.</param>
/// <param name="CreditsHeld">Credits held now: unspent, unexpired and not refunded.</param>
/// <param name="PeopleConfirmedUpcoming">People with a confirmed place at an event that has not ended.</param>
public sealed record AdminHostedEventStats(
    int OnTheSite,
    int Drafts,
    int Happened,
    int CalledOff,
    int Organizers,
    int Venues,
    int VerifiedVenues,
    int CreditsBought,
    int CreditsSpent,
    int CreditsHeld,
    int PeopleConfirmedUpcoming,
    int AppealsWaiting,
    IReadOnlyList<StatPoint> EventsCreatedPerDay,
    IReadOnlyList<StatPoint> EventsPublishedPerDay,
    IReadOnlyList<StatPoint> BookingsPerDay,
    IReadOnlyList<StatPoint> CreditsBoughtPerDay,
    IReadOnlyList<StatPoint> CreditsSpentPerDay,
    IReadOnlyList<StatSlice> EventsByState,
    IReadOnlyList<StatSlice> BookingsByStatus,
    IReadOnlyList<StatSlice> TopOrganizers,
    IReadOnlyList<StatSlice> TopVenues,
    IReadOnlyList<StatSlice> TopEventsByPeople,
    IReadOnlyList<StatSlice> EventsByRegion);

// ── the SuperAdmin's Event health tab: whether the feature is working ──────────

/// <summary>
/// Whether hosted events are working, for whoever is developing them: holds, answers, letters, errors, refusals and
/// the scheduled jobs (Ben, 2026-09-14). Counts, addresses and job names; never a person.
/// </summary>
/// <param name="HoldsLiveNow">Seat holds that have not lapsed yet.</param>
/// <param name="HoldsLapsingNextDay">Of those, the ones that lapse in the next 24 hours unless somebody answers.</param>
/// <param name="WaitingNow">Asks and holds at events still taking bookings that nobody has answered.</param>
/// <param name="OldestWaitHours">How long the longest of those has waited; null when none is waiting.</param>
/// <param name="LettersWaitingNow">Letters in the outbox not yet accepted by the mail server nor given up on. All mail.</param>
/// <param name="LettersFailedInPeriod">Letters the outbox gave up on in the window. All mail.</param>
/// <param name="TimeToAnswer">Answers given in the window, bucketed by how long the party waited.</param>
/// <param name="ErrorsPerDay">Errors the server logged on event addresses, by the server's own day; null when the log cannot be read here.</param>
/// <param name="ErrorsByAddress">The addresses those errors came from, ids replaced by <c>{id}</c>; null likewise.</param>
/// <param name="ErrorsUnavailable">Why the error panels are empty, when they are.</param>
/// <param name="RateLimitRefusals">Requests turned away by the booking and attendance limits, all time, by limit.</param>
/// <param name="JobsSinceUtc">When the job ledger began counting: the API's start.</param>
public sealed record AdminHostedEventHealth(
    int HoldsLiveNow,
    int HoldsLapsingNextDay,
    int WaitingNow,
    double? OldestWaitHours,
    int LettersWaitingNow,
    int LettersFailedInPeriod,
    IReadOnlyList<StatPoint> BookingsMadePerDay,
    IReadOnlyList<StatPoint> AnsweredPerDay,
    IReadOnlyList<StatPoint> HoldsLapsedPerDay,
    IReadOnlyList<StatSlice> TimeToAnswer,
    IReadOnlyList<StatPoint> LettersQueuedPerDay,
    IReadOnlyList<StatPoint> LettersSentPerDay,
    IReadOnlyList<StatPoint> LettersFailedPerDay,
    IReadOnlyList<StatPoint>? ErrorsPerDay,
    IReadOnlyList<StatSlice>? ErrorsByAddress,
    string? ErrorsUnavailable,
    IReadOnlyList<StatSlice> RateLimitRefusals,
    DateTime JobsSinceUtc,
    IReadOnlyList<ScheduledJobRunRecord> Jobs);

/// <summary>One scheduled job, as the ledger has seen it since the API started.</summary>
/// <param name="LastError">The first line of the most recent failure's message, if it has ever failed.</param>
public sealed record ScheduledJobRunRecord(
    string Job,
    DateTime LastStartedUtc,
    long LastDurationMs,
    bool LastSucceeded,
    string? LastError,
    DateTime? LastFailedUtc,
    int Runs,
    int Failures);

// ── the organizer's side of a removal ──────────────────────────────────────────

/// <summary>The newest removal of an event, as its organizer reads it — without the reviewer's private note.</summary>
public sealed record HostedEventRemovalRecord(
    DateTime RemovedUtc,
    bool CreditReturned,
    HostedEventAppealState AppealState,
    string? AppealMessage,
    DateTime? AppealedUtc,
    string? DecisionNote,
    DateTime? DecidedUtc);

/// <summary>The organizer's case for bringing a removed event back.</summary>
public sealed record AppealHostedEventRemovalRequest(string? Message);
