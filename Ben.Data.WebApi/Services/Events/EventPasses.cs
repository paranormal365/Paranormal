using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.Security.Cryptography;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The ticket a confirmed booking carries, and the answer a door gets when it scans one
/// (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"Generate a QR code for the confirmation the event organizer can scan
/// to check them in when they arrive so check in is smoother."</i></para>
///
/// <para><b>One pass admits the party.</b> A booking is a party, so a pass is too — the
/// alternative is four people at a door each hunting for their own code while three of them never
/// had an email address here. The scan answers with the party size so the door counts heads
/// against a number.</para>
///
/// <para><b>A pass is never edited, only replaced.</b> Changing a booking's party size or its
/// nights revokes the old pass and issues a new one, which is what makes a revoked pass a fact
/// rather than a gap: a door shown an old code is told it was replaced, and that is a different
/// sentence from being told it was never real.</para>
///
/// <para><b>The token carries nothing.</b> Not the booking, not the event, not a name — anybody
/// who photographs a printed pass over a shoulder learns a random string, and a random string is
/// worth nothing away from this door. It is looked up rather than decoded, so one cannot be
/// forged by construction.</para>
/// </remarks>
public static class EventPasses
{
    /// <summary>Whether this booking's state entitles it to a pass at all.</summary>
    /// <remarks>
    /// Only a confirmed one. A pass against a request would be a ticket to something the venue
    /// has not agreed to, and somebody would turn up holding it.
    /// </remarks>
    public static bool MayHaveAPass(HostedEventBookingStatus status)
        => status == HostedEventBookingStatus.Confirmed;

    /// <summary>The booking's live pass, or null when it has none.</summary>
    public static Task<HostedEventPass?> LiveAsync(
        BenDataContext db, Guid bookingId, CancellationToken ct)
        => db.HostedEventPasses
            .Where(p => p.HostedEventBookingId == bookingId && p.RevokedUtc == null)
            .OrderByDescending(p => p.IssuedUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Gives this booking a live pass, reusing the one it has.
    /// </summary>
    /// <remarks>
    /// <para>Idempotent on purpose. Confirming a booking issues a pass, and so does a host
    /// pressing <i>Issue</i> because they could not see one — two passes for one party is two
    /// codes at a door, one of which will be the wrong one.</para>
    ///
    /// <para>Nothing is saved here. The caller owns the transaction, so a pass cannot exist
    /// against a confirmation that was rolled back.</para>
    /// </remarks>
    public static async Task<HostedEventPass> EnsureAsync(
        BenDataContext db, HostedEventBooking booking, Guid actorId, CancellationToken ct)
    {
        if (await LiveAsync(db, booking.Id, ct) is { } live) return live;
        return Issue(db, booking.Id, actorId, replacing: null);
    }

    /// <summary>
    /// Revokes whatever is live and issues a fresh pass in its place.
    /// </summary>
    /// <remarks>
    /// Used when what the pass says stops being true — the party grew, the rooms moved — and by a
    /// host whose guest lost the letter. The new pass remembers which one it replaced, so the
    /// question "what happened to the code I was sent" always has an answer.
    /// </remarks>
    public static async Task<HostedEventPass> ReissueAsync(
        BenDataContext db, HostedEventBooking booking, Guid actorId, string reason,
        CancellationToken ct)
    {
        var old = await LiveAsync(db, booking.Id, ct);
        if (old is not null) Revoke(old, actorId, reason);

        return Issue(db, booking.Id, actorId, replacing: old?.Id);
    }

    /// <summary>Takes a pass out of use, and says why in words a door can read aloud.</summary>
    public static void Revoke(HostedEventPass pass, Guid actorId, string reason)
    {
        // The FIRST revocation is the one kept. Revoking twice is a double click, and the second
        // reason would overwrite the true one with whatever was typed later.
        if (pass.RevokedUtc is not null) return;

        pass.RevokedUtc = DateTime.UtcNow;
        pass.RevokedReason = reason.Trim() is { Length: > 0 } r ? r : "The venue withdrew this pass.";
        pass.DateUpdated = DateTime.UtcNow;
        pass.UpdatedByAppUserId = actorId;
    }

    /// <summary>
    /// Revokes every live pass a booking holds, because it no longer entitles anybody to anything.
    /// </summary>
    /// <remarks>
    /// A booking turned down or cancelled has to lose its pass in the same save, or the guest is
    /// holding a code a door would wave through. The reason is the venue's, and the guest sees it.
    /// </remarks>
    public static async Task RevokeAllAsync(
        BenDataContext db, Guid bookingId, Guid actorId, string reason, CancellationToken ct)
    {
        var live = await db.HostedEventPasses
            .Where(p => p.HostedEventBookingId == bookingId && p.RevokedUtc == null)
            .ToListAsync(ct);

        foreach (var pass in live) Revoke(pass, actorId, reason);
    }

    // ── what the door is told ────────────────────────────────────────────────

    /// <summary>
    /// Why this scan does not admit anybody, or null when it does.
    /// </summary>
    /// <remarks>
    /// <para>Each answer is a sentence somebody on a door can say to the person in front of them.
    /// "Invalid" is useless there: it does not say whether to send them to the desk, wait, or turn
    /// them away, and the person on the door is usually not the person who took the booking.</para>
    ///
    /// <para><b>A pass for a different event is told so plainly.</b> The commonest real case is a
    /// guest showing last month's code, and "that is for the October weekend" ends the
    /// conversation where "invalid" starts an argument.</para>
    /// </remarks>
    public static string? WhyThisScanIsRefused(
        HostedEventPass? pass, Guid eventIdAtThisDoor, string? otherEventName)
    {
        if (pass is null)
            return "We don't recognise that code. Ask them to check the email, or look them up by name.";

        if (pass.HostedEventBooking.HostedEventId != eventIdAtThisDoor)
            return otherEventName is { Length: > 0 } name
                ? $"That pass is for {name}, not this event."
                : "That pass is for a different event.";

        if (pass.RevokedUtc is not null)
            return pass.RevokedReason is { Length: > 0 } why
                ? $"That pass was withdrawn. {why}"
                : "That pass was withdrawn. Ask them for the current one.";

        if (!MayHaveAPass(pass.HostedEventBooking.Status))
            return pass.HostedEventBooking.Status == HostedEventBookingStatus.Requested
                ? "That booking hasn't been confirmed yet."
                : "That booking is no longer live.";

        return null;
    }

    // ── the picture ──────────────────────────────────────────────────────────

    /// <summary>
    /// The pass as a PNG, ready to show on a screen, print, or put in a letter.
    /// </summary>
    /// <param name="pixelsPerModule">
    /// How big each square of the code is drawn. Eight is a comfortable size on a phone screen and
    /// still prints; a door camera wants the picture big rather than the code dense.
    /// </param>
    /// <remarks>
    /// <para><b>Error correction M</b>, which tolerates about fifteen per cent of the code being
    /// unreadable. That is the right trade for a pass: a printed one gets folded and a screen one
    /// gets thumbprints, and the level above it makes the picture denser for a camera in bad
    /// light.</para>
    ///
    /// <para>Rendered by <c>PngByteQRCode</c>, which writes the PNG itself and needs no imaging
    /// library — so this works the same on a developer's Mac and on a Linux server, which is not
    /// true of anything built on <c>System.Drawing</c>.</para>
    /// </remarks>
    public static byte[] Png(string token, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(token, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(Math.Clamp(pixelsPerModule, 2, 20));
    }

    /// <summary>The pass as a data URI, for a letter that must carry its own picture.</summary>
    /// <remarks>
    /// An emailed pass cannot rely on a linked image: most mail clients block remote pictures
    /// until somebody clicks, and a guest at a door whose pass has not loaded has no pass. Inline
    /// bytes always draw. The letter still carries the link as well, for the client that strips
    /// data URIs instead.
    /// </remarks>
    public static string DataUri(string token, int pixelsPerModule = 6)
        => "data:image/png;base64," + Convert.ToBase64String(Png(token, pixelsPerModule));

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static HostedEventPass Issue(
        BenDataContext db, Guid bookingId, Guid actorId, Guid? replacing)
    {
        var pass = new HostedEventPass
        {
            Id = Guid.NewGuid(),
            HostedEventBookingId = bookingId,
            Token = NewToken(),
            IssuedUtc = DateTime.UtcNow,
            ReissuedFromHostedEventPassId = replacing,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = actorId,
        };
        db.HostedEventPasses.Add(pass);
        return pass;
    }

    /// <summary>256 bits, hex. Guessing one must not be a way through a door.</summary>
    /// <remarks>
    /// Hex rather than base64url, because a QR code stores upper-case alphanumerics in a denser
    /// mode than mixed case — the same secret in fewer squares, which is a code that scans in
    /// worse light.
    /// </remarks>
    private static string NewToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
