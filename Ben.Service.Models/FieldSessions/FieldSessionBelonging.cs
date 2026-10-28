namespace Ben.Service.Models.FieldSessions;

/// <summary>
/// Where a session belongs, by name, for its page to say and link to (10/28/2026).
/// </summary>
/// <remarks>
/// The session row has only ids: an investigation, a hosted event, a tour date or calendar event, a
/// place. The page showed none of them, so a session sent to an event read the same as one kept to
/// oneself. Read only for the one session being opened, never for a list.
/// </remarks>
public sealed record FieldSessionBelonging(
    /// <summary>The group the session was sent to, through its investigation or event.</summary>
    string? GroupName,
    /// <summary>For the group's public page, <c>/o/{GroupUrlName}</c>.</summary>
    string? GroupUrlName,
    string? InvestigationTitle,
    /// <summary>A hosted event's or a tour date's name.</summary>
    string? EventTitle,
    DateTime? EventStartsUtc,
    /// <summary>The event's public page, when it has one.</summary>
    string? EventUrl,
    /// <summary>A hosted event (bookings, guests), rather than a tour date or calendar event.</summary>
    bool IsHostedEvent,
    string? PlaceName,
    Guid? PlaceId,
    /// <summary>Started from the group's lead launching the night, rather than by the person alone.</summary>
    bool StartedByLaunch);
