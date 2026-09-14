using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Everything that stands between a draft and going live, as a list (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>One list, read twice.</b> The publish endpoint refuses on the first item that is not
/// done, and the event page draws the whole list as a checklist above the button. That is the
/// point: a rule the server enforces and the screen cannot see is worse than no rule, because the
/// person meets it as a mysterious refusal after they have already pressed the thing. Written once
/// here, both readers get the same sentences in the same order.</para>
///
/// <para><b>It does not check the money.</b> A slot or a credit is the last gate and is taken
/// inside the publishing transaction, because two tabs pressing the button would otherwise both
/// see one credit and both spend it. Asking about it here as well would only tell somebody what
/// they already know from the card that sells credits.</para>
///
/// <para><b>Pure, over a loaded event.</b> No database, so the checklist is free to compute and
/// trivial to test — thirteen assertions in milliseconds rather than a fixture per rule.</para>
/// </remarks>
public static class HostedEventReadiness
{
    /// <summary>
    /// Everything that has to be true, in the order somebody would fix it.
    /// </summary>
    /// <remarks>
    /// Each item's <c>Href</c> is relative to the event's own page, so the checklist can link to
    /// the card that fixes it. The website turns it into a real URL; the API has no business
    /// knowing the site's routes.
    /// </remarks>
    /// <param name="venue">
    /// The verified venue at the event's place when that is another group on this site, with this
    /// event's grant and any question still waiting on them (phase 9). Null when the place has no
    /// verified venue, or the venue is the event's own group.
    /// </param>
    public static IReadOnlyList<HostedEventReadinessItem> Describe(
        HostedEvent hosted, VenueOnTheSite? venue = null)
    {
        var noun = hosted.DatesAreSeparate ? "date" : "night";

        var items = new List<HostedEventReadinessItem>
        {
            new("Dates",
                $"At least one {noun}",
                hosted.Nights.Count > 0,
                $"Give this event at least one {noun} before publishing it. "
                + "Nobody can come to something with no date on it.",
                "#event-dates"),

            venue is null
                ? new("Venue",
                    VenueLabel(hosted),
                    VenueIsSettled(hosted),
                    VenueRefusal(hosted),
                    "#event-venue")
                : TheVenueOnTheSite(hosted, venue),

            new("Places",
                hosted.LayoutKind == HostedEventLayoutKind.Seats
                    ? "Seats on the plan, or day passes"
                    : "Rooms on the plan, or day passes",
                hosted.LayoutUnits.Count > 0 || hosted.DayPassCapacity is not 0,
                hosted.LayoutKind == HostedEventLayoutKind.Seats
                    ? "Nobody can book this yet: it has no seats on its plan and sells no day "
                    + "passes. Add a block of seats, or say how many may come for the day."
                    : "Nobody can book this yet: it has no rooms on its plan and sells no day "
                    + "passes. Place a room, or say how many may come for the day.",
                "/layout"),

            new("Contact",
                "How guests reach you and settle up",
                !string.IsNullOrWhiteSpace(hosted.ContactLine),
                "Say how a guest reaches you and how the money is settled. This site never takes "
                + "a guest's money, so if you do not say, nobody knows how to pay you.",
                "#event-details"),
        };

        return items;
    }

    /// <summary>Whether every item is done, which is what the publish button is gated on.</summary>
    public static bool IsReady(HostedEvent hosted, VenueOnTheSite? venue = null)
        => Describe(hosted, venue).All(i => i.Done);

    // ── a venue that is another group on this site (phase 9) ─────────────────

    /// <summary>
    /// The verified venue at this event's place, what it has said, and whether a question is waiting.
    /// </summary>
    public sealed record VenueOnTheSite(
        Guid OrganizationId, string Name, OrganizationVenueGrant? Grant, DateTime? AskedUtc);

    /// <summary>
    /// The one gate the site can actually enforce: the venue has said yes, to these dates.
    /// </summary>
    /// <remarks>
    /// <b>Whatever the organizer chose in the arrangement box.</b> Picking "our own venue" at a
    /// hotel that has proved it is the hotel does not make it theirs, and "arranged directly" is a
    /// claim this is the one case the site can check. So once a place has a verified venue, its yes
    /// is the only answer that settles the item.
    /// </remarks>
    private static HostedEventReadinessItem TheVenueOnTheSite(HostedEvent hosted, VenueOnTheSite venue)
    {
        string? refusal;

        if (venue.Grant is not null)
        {
            refusal = Venues.VenueGrants.WhyItDoesNotCover(
                venue.Grant, hosted.OrganizationId, hosted.PlaceId,
                hosted.Nights.Select(n => n.Date), venue.Name);
        }
        else if (venue.AskedUtc is { } asked)
        {
            refusal = $"You asked {venue.Name} on {asked:MM/dd/yyyy} and they haven't answered yet. "
                    + "They run this venue on this site, so it can't be published until they say yes.";
        }
        else
        {
            refusal = $"{venue.Name} runs this venue on this site, so they say yes to events held "
                    + "there. Ask them from the venue card on this page.";
        }

        return new("Venue", $"{venue.Name} has said yes", refusal is null, refusal ?? "", "#event-venue");
    }

    // ── the venue, which is the one with three answers ───────────────────────

    private static string VenueLabel(HostedEvent hosted) => hosted.VenueArrangement switch
    {
        HostedEventVenueArrangement.External => "Who at the venue agreed, and when",
        HostedEventVenueArrangement.PlatformGrant => "The venue has said yes",
        _ => "It is your own venue",
    };

    private static bool VenueIsSettled(HostedEvent hosted) => hosted.VenueArrangement switch
    {
        // Nothing to check: the organizer owns the building and publishing is the declaration.
        HostedEventVenueArrangement.Self => true,

        // The site cannot verify a phone call, and does not pretend to. What it can do is ask,
        // and being asked "who agreed this, and when?" is the moment somebody discovers they have
        // not actually asked anybody.
        HostedEventVenueArrangement.External =>
            !string.IsNullOrWhiteSpace(hosted.VenueContactName) && hosted.VenueAgreedOnUtc is not null,

        // Chosen at a place with no verified venue: there is nobody on the site to have said yes.
        // A place WITH one never reaches here — the venue's own item replaces this one.
        _ => false,
    };

    private static string VenueRefusal(HostedEvent hosted) => hosted.VenueArrangement switch
    {
        HostedEventVenueArrangement.External =>
            "Say who at the venue agreed to this and when they agreed it. We cannot check it for "
            + "you, but a booking nobody at the venue remembers making is the worst way to find "
            + "out on the night.",
        HostedEventVenueArrangement.PlatformGrant =>
            "Nobody runs this place as their venue on this site, so there is nobody here to ask. "
            + "Arrange it with them directly and record who agreed it.",
        _ => "",
    };
}
