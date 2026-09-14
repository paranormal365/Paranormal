using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Which colour a party wears (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"the organizer gets coloured wrist bands which mean different things
/// — blue could be the full event with food, purple the full event, red is day one … lets their
/// employees know by glance what a person is registered for."</i></para>
///
/// <para><b>Derived wherever it can be, because a venue that had to tag two hundred parties by
/// hand would tag none of them</b> — and a band nobody assigned is worse than no bands at all: a
/// steward glancing at a wrist and seeing nothing has learned something false.</para>
///
/// <para><b>Pure, and separate from the controller</b>, for the same reason the capacity rules
/// are. Three screens ask this question — the door, the board and the guest's own pass — and
/// three answers that could disagree is a steward turning somebody away over a colour.</para>
/// </remarks>
public static class EventBands
{
    /// <summary>
    /// The band a party wears, or null when the venue has nothing for them.
    /// </summary>
    /// <param name="bands">Every band this event has, in the venue's own order.</param>
    /// <param name="nightsOnTheEvent">How many nights the whole event runs.</param>
    /// <remarks>
    /// <para><b>A band chosen by hand wins</b>, always. It is the only thing the venue said out
    /// loud about this party, and the rules are a convenience for everybody they did not.</para>
    ///
    /// <para><b>Otherwise the first rule that matches, in the venue's order.</b> A party staying
    /// every night matches "every night" and would also match "some nights"; which of those they
    /// are wearing is a decision the venue makes by ordering the list, not one this code makes for
    /// them.</para>
    ///
    /// <para><b>By-hand bands are never derived onto anybody.</b> That is what by-hand means, and
    /// it is where "with food" lives until dining exists: nothing on a booking says a party is
    /// eating, so a colour that means dinner cannot be worked out from anything the site knows.
    /// </para>
    /// </remarks>
    public static HostedEventBand? For(
        HostedEventBooking booking,
        IReadOnlyList<HostedEventBand> bands,
        int nightsOnTheEvent)
    {
        if (bands.Count == 0) return null;

        if (booking.HostedEventBandId is { } chosen)
            return bands.FirstOrDefault(b => b.Id == chosen);

        var held = booking.Nights.Count(n => n.ReleasedUtc is null);

        return bands
            .OrderBy(b => b.SortOrder)
            .FirstOrDefault(band => band.Rule switch
            {
                // A day pass sleeps nowhere. A booking that holds no nights at all is the same
                // thing said a different way — a pass for the run rather than for a bed.
                HostedEventBandRule.DayPass =>
                    booking.Kind == HostedEventBookingKind.DayPass,

                // Here for the whole run. Nought nights is not "every night" however few nights
                // the event has: a day pass is not staying, and an event with no nights at all
                // has nobody staying either.
                HostedEventBandRule.EveryNight =>
                    booking.Kind != HostedEventBookingKind.DayPass
                    && held > 0 && nightsOnTheEvent > 0 && held >= nightsOnTheEvent,

                // Here for some of it — the Saturday-only guest, which is the case worth a colour
                // of its own because it is the one a steward gets wrong.
                HostedEventBandRule.SomeNights =>
                    booking.Kind != HostedEventBookingKind.DayPass
                    && held > 0 && held < nightsOnTheEvent,

                _ => false,
            });
    }
}
