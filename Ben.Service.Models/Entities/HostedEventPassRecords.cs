using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// A confirmed booking's ticket, as the guest's screen, the phone and the host's row all read it.
/// </summary>
/// <param name="Token">
/// What the QR code carries. Opaque: it holds no booking, no event and no name, so a photograph
/// of somebody else's pass is worth nothing away from that event's door.
/// </param>
/// <param name="ImageUrl">
/// Where to fetch the code as a picture. Given as well as the token so a screen never has to draw
/// one itself, and so the phone and the web show the same image.
/// </param>
public sealed record HostedEventPassRecord(
    Guid Id,
    Guid HostedEventBookingId,
    string Token,
    string ImageUrl,
    DateTime IssuedUtc,
    DateTime? RevokedUtc,
    string? RevokedReason,
    DateTime? EmailedUtc,
    DateTime? CheckedInUtc,
    string? CheckedInByName,
    bool ReplacedAnEarlierOne);

/// <summary>What a guest is shown about their own pass, and what it admits.</summary>
/// <remarks>
/// The human summary travels with the code so that a door with a flat battery, or a scanner that
/// will not focus, can still be told who this is and how many they are. A pass that only a machine
/// can read is a pass that fails on the one evening it matters.
/// </remarks>
public sealed record MyHostedEventPassRecord(
    HostedEventPassRecord Pass,
    string EventName,
    string? VenueName,
    string LeadName,
    int PartySize,
    HostedEventBookingKind Kind,
    IReadOnlyList<HostedEventBookingNightRecord> Nights,

    /// <summary>
    /// The colour to collect at the desk, when the venue uses bands (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// On the guest's own pass as well as the door's screen, so the queue moves: somebody who
    /// already knows they are blue does not have to be told.
    /// </remarks>
    HostedEventBandRecord? Band = null,

    /// <summary>
    /// Where the party sits at each sitting it has been seated for (item 235 phase 13): "Sat 10/31 · Dinner · Table 4".
    /// </summary>
    IReadOnlyList<string>? Seating = null);

/// <summary>Withdrawing a pass. The reason is required and is read out at the door.</summary>
public sealed record RevokeHostedEventPassRequest(string Reason);

/// <summary>A door scanning a code.</summary>
public sealed record ScanHostedEventPassRequest(
    string Token,
    /// <summary>
    /// False to look without admitting anybody — for a host checking a code before the doors open.
    /// </summary>
    bool CheckIn = true,

    /// <summary>
    /// Which night they are walking in on (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// Given, the scan records an arrival for that night, which is the only way a three-night
    /// weekend can answer "who was here on the Saturday". Omitted, it behaves as it did before:
    /// one stamp on the pass and no idea which night it was.
    /// </remarks>
    Guid? HostedEventNightId = null);

/// <summary>
/// What the door is told.
/// </summary>
/// <param name="Admitted">
/// Whether this code lets the party in. False always comes with <paramref name="Refusal"/> saying
/// why in words somebody can read aloud to the person in front of them.
/// </param>
/// <param name="AlreadyCheckedInUtc">
/// Set when this party had already been scanned. <b>Not a refusal</b>: a door that turned away the
/// same party walking back in from the car park would be a worse door than one that says when they
/// first arrived and lets the person on the door decide.
/// </param>
public sealed record HostedEventScanResult(
    bool Admitted,
    string? Refusal,
    Guid? HostedEventBookingId,
    string? LeadName,
    int? PartySize,
    HostedEventBookingKind? Kind,
    IReadOnlyList<HostedEventBookingNightRecord>? Nights,
    IReadOnlyList<string>? GuestNames,
    DateTime? AlreadyCheckedInUtc);

// ── the door, night by night (item 235 phase 7) ──────────────────────────────

/// <summary>
/// One party as the door sees them tonight.
/// </summary>
/// <remarks>
/// <para><b>What a steward needs and nothing else.</b> A name, how many, where they are staying,
/// whether they are already in — and dietary <b>flags</b> for tonight, which are the words a guest
/// wrote about what they cannot eat and no more than that. No address, no phone number, nothing
/// about last year.</para>
/// </remarks>
/// <param name="Code">
/// The last six of their pass, so the door can find them when somebody reads a code aloud and the
/// camera has given up.
/// </param>
/// <param name="Dietary">
/// What the party between them cannot eat, for this night. A steward pointing somebody at the
/// wrong plate is the failure this prevents; the guest's own name against each note is on the
/// kitchen's sheet, not here.
/// </param>
public sealed record HostedEventDoorPartyRecord(
    Guid HostedEventBookingId,
    string LeadName,
    int PartySize,
    HostedEventBookingKind Kind,
    string? Where,
    string? Code,
    DateTime? ArrivedUtc,
    DateTime? LeftUtc,
    int? PeopleIn,
    IReadOnlyList<string> Dietary,

    /// <summary>
    /// The colour this party wears, when the venue uses bands (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// Beside the name on the scanner, which is where Ben asked for it: a steward handing out
    /// wristbands should not have to work out which one from a list of nights.
    /// </remarks>
    HostedEventBandRecord? Band = null);

/// <summary>Somebody who turned up tonight without a booking.</summary>
/// <param name="Name">What they said their name was, if they said. A doorway is not an office.</param>
public sealed record HostedEventWalkUpRecord(
    Guid Id,
    int People,
    string? Name,
    string? Note,
    DateTime ArrivedUtc);

/// <summary>Everybody expected on one night, and how the evening is going.</summary>
/// <param name="NightId">The night being run. The door defaults to today in the venue's own zone.</param>
/// <param name="Expected">Everybody with a place tonight, arrived or not.</param>
/// <param name="WalkUps">People who turned up without one, in the order they came in.</param>
/// <param name="PlacesLeft">
/// <para>How many more people could be let in tonight, or null when the venue has set no ceiling.
/// </para>
///
/// <para>Ben, 2026-09-13, asking for exactly this: a steward with somebody in front of them
/// offering cash needs one number, now, and "look at the plan and count" is not an answer in a
/// doorway. It counts everything that takes a place — confirmed parties here tonight and walk-ups
/// already in — against whichever ceiling this event has.</para>
/// </param>
/// <param name="PlacesLeftSentence">
/// The same fact in words, because "3" beside a heading is a number a tired steward can read as
/// anything. Null when there is no ceiling to speak of.
/// </param>
public sealed record HostedEventDoorRecord(
    Guid HostedEventId,
    string EventName,
    Guid NightId,
    DateTime NightDate,
    IReadOnlyList<HostedEventNightRecord> Nights,
    IReadOnlyList<HostedEventDoorPartyRecord> Expected,
    int PeopleExpected,
    int PeopleIn,
    IReadOnlyList<HostedEventWalkUpRecord> WalkUps,
    int? PlacesLeft = null,
    string? PlacesLeftSentence = null);

/// <summary>Writing down somebody who turned up without a booking.</summary>
public sealed record HostedEventWalkUpRequest(
    Guid HostedEventNightId,
    int People = 1,
    string? Name = null,
    string? Note = null);

/// <summary>Marking somebody in, out, or not here after all.</summary>
/// <param name="People">
/// How many actually came, when it is not the whole party. Null means all of them, which is the
/// common case and keeps the number in step with a booking the host may still edit.
/// </param>
public sealed record HostedEventDoorMoveRequest(
    Guid HostedEventBookingId,
    Guid HostedEventNightId,
    int? People = null);

// ── what a party wears (item 235 phase 7) ────────────────────────────────────

/// <summary>
/// A colour a party wears, and what it means.
/// </summary>
/// <remarks>
/// <b>Both, always.</b> A chip that is only a colour is useless to the one steward in twelve who
/// cannot separate red from green, which is the same reason every square on a plan carries a word
/// as well as a hue. The swatch is optional and the name is not.
/// </remarks>
public sealed record HostedEventBandRecord(
    Guid Id,
    string Colour,
    string Meaning,
    string? Hex,
    HostedEventBandRule Rule,
    int SortOrder);

/// <summary>Every band an event has, in the venue's own order.</summary>
/// <remarks>
/// The order matters and is not decoration: the first rule that matches a party is the band they
/// wear, so a venue puts "the whole weekend" above "some of it" and the derivation follows.
/// </remarks>
public sealed record HostedEventBandsRecord(
    Guid HostedEventId,
    IReadOnlyList<HostedEventBandRecord> Bands);

/// <summary>Replacing the whole set, the way the menus and the plan are replaced.</summary>
public sealed record SetHostedEventBandsRequest(IReadOnlyList<HostedEventBandInput> Bands);

/// <summary>One band to write.</summary>
public sealed record HostedEventBandInput(
    string Colour,
    string Meaning,
    string? Hex = null,
    HostedEventBandRule Rule = HostedEventBandRule.ByHand,
    Guid? Id = null);

/// <summary>Giving one party a band by hand, or taking theirs away.</summary>
/// <param name="HostedEventBandId">Null puts them back on the rules.</param>
public sealed record SetHostedEventBookingBandRequest(Guid? HostedEventBandId);
