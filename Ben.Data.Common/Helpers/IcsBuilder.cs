using System.Globalization;
using System.Text;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// One event, written as an iCalendar file a mail client will offer to add to a calendar.
/// </summary>
/// <remarks>
/// <para>Built for item 233. <b>Ben, 2026-09-10:</b> the guest mail should include "the .ics file
/// with information for their tour" — because the thing a guest actually needs a week later is
/// their phone reminding them, not an email they have to find again.</para>
///
/// <para><b>No package.</b> RFC 5545 is large, and almost all of it is recurrence, timezone
/// definitions and scheduling negotiation that this product does not do. What is left is a
/// handful of lines with careful escaping, and a library here would be a dependency carried for
/// four properties.</para>
///
/// <para><b>Times are written in UTC.</b> A local time in an .ics is only correct if the file also
/// carries the VTIMEZONE definition for that zone — hundreds of lines, and wrong the year a
/// government changes its daylight-saving rules. UTC is unambiguous, and every calendar shows it
/// to the reader in their own zone, which is what they wanted anyway.</para>
/// </remarks>
public static class IcsBuilder
{
    /// <summary>Where a calendar file is downloaded to, and what a mail attachment is called.</summary>
    public const string ContentType = "text/calendar; charset=utf-8; method=PUBLISH";

    /// <summary>
    /// One event's worth of calendar.
    /// </summary>
    /// <param name="Uid">
    /// The event's stable identity. A second mail carrying the same uid UPDATES the entry in the
    /// guest's calendar rather than adding a duplicate, which is why it must be the event's id and
    /// not a fresh one per send.
    /// </param>
    /// <param name="Sequence">
    /// Raised when the details change, so a calendar knows the later file wins. Zero is the
    /// original invitation.
    /// </param>
    public sealed record IcsEvent(
        string Uid,
        DateTime StartUtc,
        DateTime EndUtc,
        string Summary,
        string? Description = null,
        string? Location = null,
        string? Url = null,
        string? OrganizerName = null,
        string? OrganizerEmail = null,
        decimal? Latitude = null,
        decimal? Longitude = null,
        int Sequence = 0,
        DateTime? StampUtc = null);

    /// <summary>The file's bytes, ready to attach.</summary>
    public static byte[] BuildBytes(IcsEvent calendarEvent)
        => Encoding.UTF8.GetBytes(Build(calendarEvent));

    /// <summary>
    /// Several entries in one file, ready to attach.
    /// </summary>
    /// <remarks>
    /// A weekend at a hotel is not one calendar entry (item 235): a guest who booked Friday and
    /// Saturday wants two nights in their diary, each with the room they are in, so that looking
    /// at Saturday tells them where they are sleeping. One entry spanning both would sit across
    /// the whole weekend as a single block and say nothing about either night.
    /// </remarks>
    public static byte[] BuildBytes(IReadOnlyList<IcsEvent> calendarEvents)
        => Encoding.UTF8.GetBytes(Build(calendarEvents));

    /// <summary>The file's text.</summary>
    public static string Build(IcsEvent e) => Build([e]);

    /// <summary>
    /// The file's text, with one entry per event.
    /// </summary>
    /// <remarks>
    /// Each entry keeps its OWN uid, because that is what a calendar updates against. Sharing one
    /// uid across the nights of a weekend would make every night overwrite the last and leave a
    /// guest with a single entry for a three-night stay.
    /// </remarks>
    public static string Build(IReadOnlyList<IcsEvent> events)
    {
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//IsHaunted//Tours//EN",
            "CALSCALE:GREGORIAN",
            // PUBLISH, not REQUEST: this is an invitation to something already happening, not a
            // meeting being negotiated. REQUEST makes clients offer accept/decline buttons whose
            // replies would go nowhere — the sign-up already happened on the site.
            "METHOD:PUBLISH",
        };

        foreach (var e in events) lines.AddRange(EventLines(e));

        lines.Add("END:VCALENDAR");

        var text = new StringBuilder();
        foreach (var line in lines) text.Append(Fold(line)).Append("\r\n");
        return text.ToString();
    }

    /// <summary>One VEVENT block.</summary>
    private static List<string> EventLines(IcsEvent e)
    {
        var lines = new List<string>
        {
            "BEGIN:VEVENT",
            $"UID:{Escape(e.Uid)}",
            $"SEQUENCE:{e.Sequence}",
            $"DTSTAMP:{Stamp(e.StampUtc ?? DateTime.UtcNow)}",
            $"DTSTART:{Stamp(e.StartUtc)}",
            // An event that ends before it starts is refused by some clients and silently shown as
            // a point in time by others; an hour is a better guess than either.
            $"DTEND:{Stamp(e.EndUtc > e.StartUtc ? e.EndUtc : e.StartUtc.AddHours(1))}",
            $"SUMMARY:{Escape(e.Summary)}",
        };

        if (!string.IsNullOrWhiteSpace(e.Description)) lines.Add($"DESCRIPTION:{Escape(e.Description)}");
        if (!string.IsNullOrWhiteSpace(e.Location)) lines.Add($"LOCATION:{Escape(e.Location)}");
        if (!string.IsNullOrWhiteSpace(e.Url)) lines.Add($"URL:{Escape(e.Url)}");

        if (e.Latitude is { } lat && e.Longitude is { } lon)
            lines.Add($"GEO:{Number(lat)};{Number(lon)}");

        if (!string.IsNullOrWhiteSpace(e.OrganizerEmail))
        {
            var name = string.IsNullOrWhiteSpace(e.OrganizerName) ? null : $";CN={Escape(e.OrganizerName)}";
            lines.Add($"ORGANIZER{name}:mailto:{e.OrganizerEmail.Trim()}");
        }

        lines.Add("END:VEVENT");
        return lines;
    }

    /// <summary>UTC, in the basic form the format wants: <c>20260913T190000Z</c>.</summary>
    private static string Stamp(DateTime value)
        => value.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Number(decimal value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes the four characters the format gives meaning to.
    /// </summary>
    /// <remarks>
    /// The backslash goes first, or every escape this method adds gets escaped again by the ones
    /// after it. A raw comma is what turns "Nashville, TN" into two properties and a broken file.
    /// </remarks>
    private static string Escape(string? value)
        => (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n")
            .Replace(";", "\\;")
            .Replace(",", "\\,");

    /// <summary>
    /// Folds a long line the way the format requires: 75 octets, then a break and a leading space.
    /// </summary>
    /// <remarks>
    /// Counted in <b>octets</b>, not characters, and never split inside one: a description with an
    /// em dash in it is three bytes for one character, and folding through the middle of it hands
    /// the calendar a byte sequence that is not text. Clients that survive a long unfolded line are
    /// common enough that this is easy to get away with until the one that does not.
    /// </remarks>
    private static string Fold(string line)
    {
        var bytes = Encoding.UTF8.GetByteCount(line);
        if (bytes <= 75) return line;

        var folded = new StringBuilder();
        var used = 0;
        var first = true;

        foreach (var rune in line.EnumerateRunes())
        {
            var size = Encoding.UTF8.GetByteCount(rune.ToString());
            // Continuation lines start with a space, which costs one of their 75 octets.
            var limit = first ? 75 : 74;
            if (used + size > limit)
            {
                folded.Append("\r\n ");
                used = 0;
                first = false;
            }
            folded.Append(rune.ToString());
            used += size;
        }

        return folded.ToString();
    }
}
