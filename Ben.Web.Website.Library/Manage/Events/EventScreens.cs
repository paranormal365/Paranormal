namespace Ben.Web.Website.Library.Manage.Events;

/// <summary>
/// Every screen that belongs to one hosted event, in the order somebody running one works through them.
/// </summary>
/// <remarks>
/// <para>The SuperAdmin's list of events opens a row onto these, so any event on the site is one click from its board,
/// its door or its files (Ben, 2026-09-14: oversight of every hosted event screen). A guard test reads the
/// <c>@page</c> directives under <c>Manage/Events</c> and fails when an event screen exists that this list does not
/// name, so a new screen cannot be left out of the SuperAdmin's reach.</para>
///
/// <para>The public page and the photo wall are addressed by the group's and event's own names or the event alone,
/// so they are built separately from the organizer's screens.</para>
/// </remarks>
public static class EventScreens
{
    /// <summary>One organizer's screen: what it is called and the address after the event's own.</summary>
    /// <param name="Suffix">Empty for the event's page itself.</param>
    public sealed record Screen(string Label, string Suffix)
    {
        public string Href(Guid orgId, Guid eventId) => $"/organizations/{orgId}/events/{eventId}{Suffix}";
    }

    public static readonly IReadOnlyList<Screen> Organizer =
    [
        new("The event", ""),
        new("Plan", "/layout"),
        new("Bookings", "/bookings"),
        new("Menus", "/menus"),
        new("Dining tables", "/dining"),
        new("What the kitchen needs", "/dietary"),
        new("Programme", "/sessions"),
        new("Staff", "/staff"),
        new("The door", "/door"),
        new("Bands", "/bands"),
        new("Files", "/files"),
        new("Gallery", "/gallery"),
        new("After the event", "/after"),
        new("Keep the files", "/keep"),
        new("Copy this event", "/copy"),
    ];

    /// <summary>The page a visitor reads.</summary>
    public static string PublicPage(string orgUrlName, string eventUrlName) => $"/o/{orgUrlName}/events/{eventUrlName}";

    /// <summary>The wall of photographs guests put up during the event.</summary>
    public static string PhotoWall(Guid eventId) => $"/events/{eventId}/wall";
}
