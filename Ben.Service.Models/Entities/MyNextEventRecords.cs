namespace Ben.Service.Models.Entities;

/// <summary>What sort of thing a signed-in person is going to (home countdown, 10/16/2026).</summary>
public enum MyNextEventKind
{
    /// <summary>A booking at a hosted event: a weekend, a dinner, a show.</summary>
    HostedEvent = 0,

    /// <summary>A calendar event they said they are coming to.</summary>
    Event = 1,

    /// <summary>A seat on a tour date.</summary>
    TourDate = 2,

    /// <summary>An investigation they are on the team of.</summary>
    Investigation = 3,
}

/// <summary>Where the person stands with it, in the words the screens that own it already use.</summary>
public enum MyNextEventStanding
{
    /// <summary>They have a place: a confirmed booking, a reserved seat, an accepted RSVP.</summary>
    Going = 0,

    /// <summary>They asked, and nobody has answered yet. Nothing is held.</summary>
    AskedFor = 1,

    /// <summary>A hosted event's hold: places set aside while the venue decides.</summary>
    HeldForYou = 2,

    /// <summary>On an investigation's team and not yet answered.</summary>
    Invited = 3,
}

/// <summary>
/// One thing the signed-in person is going to, with the moment it starts and ends in UTC.
/// </summary>
/// <param name="StartsAtUtc">Stored UTC; the card shows it on <paramref name="TimeZoneId"/>'s clock.</param>
/// <param name="EndsAtUtc">When it stops being "Happening now" and the card moves on.</param>
/// <param name="Href">Where a tap goes: the pass for a confirmed booking, the event's page otherwise.</param>
/// <param name="SignedUpUtc">When they signed up, where the record says: the start of the card's progress bar.</param>
public sealed record MyNextEventItem(
    MyNextEventKind Kind,
    Guid Id,
    string Title,
    string? OrganizationName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string? TimeZoneId,
    string Href,
    MyNextEventStanding Standing,
    DateTime? SignedUpUtc);

/// <summary>
/// The next few things the signed-in person is going to, soonest first, nothing already over.
/// </summary>
/// <param name="Items">At most a handful: the card shows one and moves to the next as each ends.</param>
/// <param name="Total">How many there are in all, for "and 2 more".</param>
public sealed record MyNextEventsResponse(IReadOnlyList<MyNextEventItem> Items, int Total);
