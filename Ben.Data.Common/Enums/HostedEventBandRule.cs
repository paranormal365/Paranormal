namespace Ben.Data.Common.Enums;

/// <summary>
/// How a party gets a band, without anybody tagging two hundred of them by hand
/// (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-13: <i>"the organizer gets coloured wrist bands which mean different things
/// — blue could be the full event with food, purple the full event, red is day one … lets their
/// employees know by glance what a person is registered for."</i></para>
///
/// <para><b>Derived wherever it can be.</b> A venue that has to tag every party by hand will tag
/// none of them, and a band nobody assigned is worse than no bands at all — a steward glancing at
/// a wrist and seeing nothing has learned something false.</para>
///
/// <para><b>The first rule that matches wins</b>, in the venue's own order, which is why the rows
/// are sorted and not searched: a party staying every night matches "every night" and might also
/// match "one night", and the venue decides which of those they are wearing.</para>
/// </remarks>
public enum HostedEventBandRule
{
    /// <summary>Here for every night of the run.</summary>
    EveryNight = 0,

    /// <summary>Here for some nights but not all — the Saturday-only guest.</summary>
    SomeNights = 1,

    /// <summary>Here for the day and not staying.</summary>
    DayPass = 2,

    /// <summary>
    /// Given out by hand, party by party.
    /// </summary>
    /// <remarks>
    /// The escape hatch, and the home of "with food" until dining lands: nothing on a booking says
    /// a party is eating, so a band that means dinner cannot be derived from anything the site
    /// knows today.
    /// </remarks>
    ByHand = 3,
}
