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
    IReadOnlyList<HostedEventBookingNightRecord> Nights);

/// <summary>Withdrawing a pass. The reason is required and is read out at the door.</summary>
public sealed record RevokeHostedEventPassRequest(string Reason);

/// <summary>A door scanning a code.</summary>
public sealed record ScanHostedEventPassRequest(
    string Token,
    /// <summary>
    /// False to look without admitting anybody — for a host checking a code before the doors open.
    /// </summary>
    bool CheckIn = true);

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
