namespace Ben.Service.Models.FieldLaunches;

/// <summary>
/// A launch as the app and the website read it (item 252): what was started, when it goes, and
/// the link that opens Field Kit on it.
/// </summary>
/// <param name="Id">The launch.</param>
/// <param name="Target"><c>investigation</c>, <c>event</c> (a tour date or calendar event) or <c>hosted-event</c>.</param>
/// <param name="InvestigationId">Set for an investigation — the session is filed under it.</param>
/// <param name="OrgCalendarEventId">Set for a tour date or calendar event.</param>
/// <param name="HostedEventId">Set for a hosted event.</param>
/// <param name="Title">What it is called.</param>
/// <param name="LocationLabel">Where, for the session's label; null when the exact place is withheld.</param>
/// <param name="OrganizationName">The group running it.</param>
/// <param name="LaunchedByName">Who pressed Launch.</param>
/// <param name="LaunchedUtc">When.</param>
/// <param name="EndsUtc">When the thing ends.</param>
/// <param name="ExpiresUtc">When the link stops opening it — six hours after the end.</param>
/// <param name="IsPublic">Whether the card is for anyone, or only the people it was sent to.</param>
/// <param name="AppLink">Opens Field Kit on it: <c>ishaunted://field-kit/launch/{id}</c>.</param>
public sealed record FieldLaunchRecord(
    Guid Id,
    string Target,
    Guid? InvestigationId,
    Guid? OrgCalendarEventId,
    Guid? HostedEventId,
    string Title,
    string? LocationLabel,
    string OrganizationName,
    string LaunchedByName,
    DateTime LaunchedUtc,
    DateTime EndsUtc,
    DateTime ExpiresUtc,
    bool IsPublic,
    string AppLink);

/// <summary>Something the caller may launch now, for the lead's list.</summary>
/// <param name="Target"><c>investigation</c>, <c>event</c> or <c>hosted-event</c>.</param>
/// <param name="Id">The investigation, calendar event or hosted event.</param>
/// <param name="Title">What it is called.</param>
/// <param name="StartsUtc">When it starts.</param>
/// <param name="EndsUtc">When it ends.</param>
/// <param name="TimeZoneId">Its own clock, when it names one.</param>
/// <param name="People">How many people are registered — who a launch would go to.</param>
/// <param name="LastLaunchedUtc">When it was last launched, if it has been.</param>
/// <param name="IsPublic">Whether its card would be public.</param>
public sealed record LaunchableRecord(
    string Target,
    Guid Id,
    string Title,
    DateTime StartsUtc,
    DateTime EndsUtc,
    string? TimeZoneId,
    int People,
    DateTime? LastLaunchedUtc,
    bool IsPublic);

/// <summary>Pressing Launch.</summary>
/// <param name="Target"><c>investigation</c>, <c>event</c> or <c>hosted-event</c>.</param>
/// <param name="Id">Which one.</param>
public sealed record LaunchRequest(string Target, Guid Id);

/// <summary>What a launch did, in the words the lead is told.</summary>
/// <param name="Launch">The launch itself.</param>
/// <param name="People">How many people it was for.</param>
/// <param name="PeopleWithTheApp">How many of them are signed in on the app.</param>
/// <param name="PhonesReached">Phones Apple accepted the push for.</param>
/// <param name="PushConfigured">False on a server with no push key: the card went up, nobody was pushed.</param>
public sealed record LaunchOutcomeRecord(
    FieldLaunchRecord Launch, int People, int PeopleWithTheApp, int PhonesReached, bool PushConfigured);

/// <summary>A feed card's launch (item 252): its title, the app link, and when it goes.</summary>
public sealed record FeedLaunchCard(Guid LaunchId, string Title, string AppLink, DateTime ExpiresUtc);
