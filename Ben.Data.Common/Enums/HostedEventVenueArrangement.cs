namespace Ben.Data.Common.Enums;

/// <summary>
/// How this event came to be allowed to happen at its venue (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>Ben asked the question this answers</b> on 2026-09-12: "how do you think we confirm an
/// event is happening… Does Organizer get confirmation from venue, does venue have any
/// responsibilities in the experience, or is that the organizer's decision? I am not sure how this
/// gets handled." The answer he agreed to is that it depends on whose venue it is, and that the
/// site should ask rather than assume — because the three cases have genuinely different truths
/// and pretending otherwise means either nagging a hotel that owns its own building or letting
/// somebody publish an event at a venue that has never heard of them.</para>
///
/// <para><b>Recording it is not enforcing it.</b> For a venue off the platform the site cannot
/// verify anything, and does not pretend to: it records what the organizer says they arranged, and
/// that record is what a support conversation starts from. Only the third case is a gate, because
/// only there is there another group on this site able to answer.</para>
///
/// <para><b>Append only.</b> The numbers are stored.</para>
/// </remarks>
public enum HostedEventVenueArrangement
{
    /// <summary>
    /// The venue is the organizer's own. Publishing is the whole declaration.
    /// </summary>
    /// <remarks>
    /// Zero, and the right default: the first hosted events are hotels running their own weekends,
    /// and asking a hotel to confirm that it has permission to use itself would be the site being
    /// silly at somebody.
    /// </remarks>
    Self = 0,

    /// <summary>
    /// Somebody else's venue, arranged off this site — a phone call, an email, a signed hire.
    /// </summary>
    /// <remarks>
    /// Needs a name and a date before publishing. Not because the site can check either, but
    /// because being asked "who agreed this, and when?" is the moment an organizer discovers they
    /// have not actually asked anybody.
    /// </remarks>
    External = 1,

    /// <summary>
    /// The venue is another group's published profile on this site, and it has said yes.
    /// </summary>
    /// <remarks>
    /// The only one the site can enforce, and the only one it does: publishing needs a grant from
    /// that group covering every night. Refused in words until phase 9 builds venue profiles, so
    /// that the choice is visible and honest rather than absent and confusing.
    /// </remarks>
    PlatformGrant = 2,
}
