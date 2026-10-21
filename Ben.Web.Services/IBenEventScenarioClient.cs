using Ben.Service.Models.Entities;
using Ben.Service.Models.FieldLaunches;
using Ben.Web.Services.WebApi;

namespace Ben.Web.Services;

/// <summary>
/// What Jim's overnight conference needed and the site didn't have (event-scenario-gaps, 10/21/2026):
/// inviting a list and sending tickets, selling at the door, marking who came to a class, starting the
/// sessions from the website, the results letter, the results the app reads, and a tour date's room.
/// </summary>
/// <remarks>
/// Reads return <see cref="ItemResult{T}"/> so a refusal never reads as "nothing here"; writes return
/// <c>(Result, Error)</c> and keep the server's sentence.
/// </remarks>
public interface IBenEventScenarioClient
{
    /// <summary>The sender agrees (or withdraws agreeing) that their session may be shown with an event's results.</summary>
    Task<(bool Done, string? Error)> SetSessionResultsConsentAsync(Guid sessionId, bool agree, CancellationToken token = default);

    /// <summary>The organizer shows a sent session with the event's results, or stops.</summary>
    Task<(bool Done, string? Error)> SetSessionShownWithResultsAsync(Guid sessionId, bool show, CancellationToken token = default);

    /// <summary>The sessions shown with a hosted event's results, for its public page.</summary>
    Task<LoadResult<Ben.Service.Models.FieldSessions.PublicEventSessionRow>> GetEventResultSessionsAsync(Guid hostedEventId, CancellationToken token = default);

    /// <summary>Upcoming public hosted events for the home map, at approximate points.</summary>
    Task<LoadResult<PublicHostedEventMapPin>> GetHostedEventMapPinsAsync(CancellationToken token = default);

    /// <summary>Invites a pasted list, or sends each of them a ticket, with the organizer's own words.</summary>
    Task<(HostedEventGuestInvitesRecord? Result, string? Error)> InviteEventGuestsAsync(
        Guid orgId, Guid eventId, InviteHostedEventGuestsRequest request, CancellationToken token = default);

    /// <summary>Says whether a party came, after the event: true, false, or null for what the door recorded.</summary>
    Task<(HostedEventAfterRecord? Result, string? Error)> SetGuestCameAsync(
        Guid orgId, Guid eventId, Guid bookingId, bool? came, CancellationToken token = default);

    /// <summary>Sends "the results are ready", with a link and a QR code, to everybody with a place.</summary>
    Task<(HostedEventResultsSentRecord? Result, string? Error)> SendEventResultsAsync(
        Guid orgId, Guid eventId, SendHostedEventResultsRequest request, CancellationToken token = default);

    /// <summary>The door's ticket code and who has asked by scanning it.</summary>
    Task<ItemResult<HostedEventDoorTicketsRecord>> GetDoorTicketsAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>Turns tickets at the door on (a new code) or off.</summary>
    Task<(HostedEventDoorTicketsRecord? Result, string? Error)> SetDoorTicketsAsync(
        Guid orgId, Guid eventId, bool on, CancellationToken token = default);

    /// <summary>Confirms a place asked for at the door, once paid.</summary>
    Task<(HostedEventDoorTicketsRecord? Result, string? Error)> ConfirmDoorTicketAsync(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken token = default);

    /// <summary>What the door's code shows the person who scanned it.</summary>
    Task<ItemResult<PublicDoorTicketRecord>> GetDoorTicketAsync(string doorToken, CancellationToken token = default);

    /// <summary>Asks for a place at the door.</summary>
    Task<(PublicDoorTicketRecord? Result, string? Error)> AskAtTheDoorAsync(
        string doorToken, AskAtTheDoorRequest request, CancellationToken token = default);

    /// <summary>The classes that take sign-ups, with their rosters, for the door or the group.</summary>
    Task<ItemResult<HostedEventClassesRecord>> GetClassAttendanceAsync(Guid orgId, Guid eventId, CancellationToken token = default);

    /// <summary>Marks somebody there at a class, or takes the mark off.</summary>
    Task<(HostedEventSessionRosterRecord? Result, string? Error)> SetSessionAttendanceAsync(
        Guid orgId, Guid eventId, Guid sessionId, Guid signUpId, bool attended, CancellationToken token = default);

    /// <summary>
    /// Starts the event's sessions: everybody with a place is told sessions are being accepted.
    /// </summary>
    /// <returns>The outcome, or the server's sentence when it refused.</returns>
    Task<(LaunchOutcomeRecord? Result, string? Error)> StartHostedEventSessionsAsync(
        Guid hostedEventId, CancellationToken token = default);

    /// <summary>A tour date's room, for somebody checked in on the walk or running it.</summary>
    Task<ItemResult<TourRoomRecord>> GetTourRoomAsync(Guid dateId, CancellationToken token = default);

    /// <summary>Posts in a tour date's room.</summary>
    Task<(TourRoomRecord? Result, string? Error)> PostTourRoomAsync(Guid dateId, string body, CancellationToken token = default);

    /// <summary>Takes a post down from a tour date's room.</summary>
    Task<(TourRoomRecord? Result, string? Error)> RemoveTourRoomMessageAsync(Guid dateId, Guid messageId, CancellationToken token = default);
}
