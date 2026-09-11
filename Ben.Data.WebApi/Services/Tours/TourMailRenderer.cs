using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Ben.Service.Models.Entities;

namespace Ben.Data.WebApi.Services.Tours;

/// <summary>
/// Turns a business's own wording into the mail one guest gets about one date (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "We can let them create an e-mail template to generate for people
/// who sign up and then as a reminder", and "account for creating the email template with where
/// the data about the tour is located. Like the start date and time and name and photo of person
/// who will be leading the tour - for safety."</para>
///
/// <para><b>Placeholders, not a language.</b> <c>{{tour.name}}</c> is replaced; there are no
/// conditionals, no loops and no expressions. A business writing its welcome mail should not be
/// able to write a program, and a template engine in a mail path is a place for one tenant's text
/// to reach another tenant's data.</para>
///
/// <para><b>Everything is HTML-encoded on the way in</b>, except the two values that are already
/// sanitized markup and the guide photographs this class builds itself. A guest called
/// <c>&lt;script&gt;</c> is a name, not an instruction.</para>
///
/// <para><b>The meeting point is always there.</b> Ben's rule is that the start address is in the
/// mail; a template that leaves it out still gets it, appended, because a guest who cannot tell
/// where to stand has not been told about the tour.</para>
/// </remarks>
public static class TourMailRenderer
{
    /// <summary>Everything a tour mail can say, gathered once.</summary>
    /// <param name="GuidePhotos">Absolute URLs, already public. Empty when nobody has published one.</param>
    public sealed record TourMailFacts(
        string TourName,
        string? TourDescriptionHtml,
        string MeetingPoint,
        string? MeetingPointMapUrl,
        int? DurationMinutes,
        DateTime StartUtc,
        DateTime EndUtc,
        string TimeZoneId,
        int? Capacity,
        int? SpacesLeft,
        string DateTitle,
        string? DateUrl,
        IReadOnlyList<string> GuideNames,
        IReadOnlyList<(string Name, string PhotoUrl)> GuidePhotos,
        string? GuestName,
        string BusinessName,
        string? BusinessUrl,
        string? ContactLine,
        string SiteName);

    /// <summary>What comes out: a subject and a body, ready to send.</summary>
    public sealed record Rendered(string Subject, string HtmlBody);

    /// <summary>The wording a tour gets when its business has not written its own.</summary>
    /// <remarks>
    /// Deliberately complete rather than minimal: a business that never opens the editor should
    /// still send a mail that says where to stand, when, who is leading and how to pay — which is
    /// the whole of what Ben asked the mail to carry.
    /// </remarks>
    public const string DefaultSubject = "You're coming on {{tour.name}}";

    public const string DefaultBody = """
        <p>Hello {{guest.name}},</p>
        <p>You're booked on <strong>{{tour.name}}</strong> with {{business.name}}.</p>
        <p><strong>When:</strong> {{date.start}}<br />
        <strong>Where you meet:</strong> {{tour.meetingPoint}}<br />
        <strong>How long:</strong> {{tour.length}}</p>
        {{guide.block}}
        {{business.contactBlock}}
        <p>The calendar file attached puts this in your diary, with the meeting point.</p>
        <p>— {{site.name}}</p>
        """;

    private static readonly Regex Placeholder = new(@"\{\{\s*([a-zA-Z.]+)\s*\}\}", RegexOptions.Compiled);

    /// <summary>Renders one mail. Null template parts fall back to the built-in wording.</summary>
    public static Rendered Render(string? subjectTemplate, string? bodyTemplate, TourMailFacts facts)
    {
        var values = Values(facts);

        var subject = Substitute(
            string.IsNullOrWhiteSpace(subjectTemplate) ? DefaultSubject : subjectTemplate, values);
        var body = Substitute(
            string.IsNullOrWhiteSpace(bodyTemplate) ? DefaultBody : bodyTemplate, values);

        // Ben's rule: the address of the tour start is in the email. A business that deleted the
        // placeholder gets it back rather than sending a guest out with nowhere to go.
        //
        // Compared against the ENCODED address, because that is what the body contains. Comparing
        // the raw one meant an address with an apostrophe or an ampersand in it — "O'Connor
        // Street" — never matched what had just been rendered from it, and the guest read the
        // meeting point twice.
        if (!body.Contains(Encode(facts.MeetingPoint), StringComparison.OrdinalIgnoreCase)
            && !body.Contains(facts.MeetingPoint, StringComparison.OrdinalIgnoreCase))
            body += $"\n<p><strong>Where you meet:</strong> {Encode(facts.MeetingPoint)}</p>";

        return new Rendered(subject.Trim(), body.Trim());
    }

    /// <summary>The same mail, worded as the reminder the night before.</summary>
    /// <remarks>
    /// The business writes one template, not two: what a guest needs the day before is what they
    /// needed when they signed up, plus the fact that it is tomorrow. Only the subject changes,
    /// and only when it does not already say so.
    /// </remarks>
    public static Rendered RenderReminder(string? subjectTemplate, string? bodyTemplate, TourMailFacts facts)
    {
        var rendered = Render(subjectTemplate, bodyTemplate, facts);
        return rendered.Subject.Contains("tomorrow", StringComparison.OrdinalIgnoreCase)
            ? rendered
            : rendered with { Subject = $"Tomorrow: {rendered.Subject}" };
    }

    /// <summary>Every placeholder a business may use — the same list the editor shows.</summary>
    /// <remarks>
    /// It lives in the shared contracts rather than here, because the editor that lists them runs
    /// on the website and cannot see this project. One list, so what a business is offered and
    /// what actually resolves cannot drift apart.
    /// </remarks>
    public static IReadOnlyList<(string Token, string Means)> Tokens => TourMailTokens.All;

    private static Dictionary<string, string> Values(TourMailFacts f)
    {
        var zone = ZoneOf(f.TimeZoneId);
        var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(f.StartUtc, DateTimeKind.Utc), zone);
        var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(f.EndUtc, DateTimeKind.Utc), zone);
        var abbreviation = zone.IsDaylightSavingTime(start) ? zone.DaylightName : zone.StandardName;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tour.name"] = Encode(f.TourName),
            // Already sanitized markup when the business wrote it in the editor; encoding it again
            // would show a guest their own paragraph tags.
            ["tour.description"] = f.TourDescriptionHtml ?? string.Empty,
            ["tour.meetingPoint"] = Encode(f.MeetingPoint),
            ["tour.meetingPointMap"] = f.MeetingPointMapUrl is { Length: > 0 } map
                ? $"<a href=\"{Encode(map)}\">{Encode(f.MeetingPoint)}</a>"
                : Encode(f.MeetingPoint),
            ["tour.length"] = f.DurationMinutes is { } minutes ? Length(minutes) : "",
            // US format, as the site uses everywhere, and the zone named so nobody has to guess
            // whose seven o'clock it is.
            ["date.start"] = $"{start.ToString("dddd, MM/dd/yyyy", CultureInfo.InvariantCulture)} at "
                           + $"{start.ToString("h:mm tt", CultureInfo.InvariantCulture)} {Short(abbreviation)}",
            ["date.end"] = $"{end.ToString("h:mm tt", CultureInfo.InvariantCulture)} {Short(abbreviation)}",
            ["date.day"] = start.ToString("dddd, MM/dd/yyyy", CultureInfo.InvariantCulture),
            ["date.capacity"] = f.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["date.spacesLeft"] = f.SpacesLeft?.ToString(CultureInfo.InvariantCulture) ?? "",
            ["date.title"] = Encode(f.DateTitle),
            ["date.url"] = f.DateUrl is { Length: > 0 } url ? $"<a href=\"{Encode(url)}\">{Encode(url)}</a>" : "",
            ["guide.names"] = Encode(Join(f.GuideNames)),
            ["guide.photos"] = Photos(f.GuidePhotos),
            // The whole labelled paragraph, or nothing at all. A date with nobody on it yet used
            // to read "Your guide: your guide" — a label with its own fallback inside it — and an
            // empty contact line left a bare <p></p> in the mail. Blocks disappear; values do not,
            // because a value still has to say something inside somebody else's sentence.
            ["guide.block"] = f.GuideNames.Count == 0 && f.GuidePhotos.Count == 0
                ? string.Empty
                : $"<p><strong>{(f.GuideNames.Count > 1 ? "Your guides" : "Your guide")}:</strong> "
                  + $"{Encode(Join(f.GuideNames))}</p>{Photos(f.GuidePhotos)}",
            ["business.contactBlock"] = string.IsNullOrWhiteSpace(f.ContactLine)
                ? string.Empty
                : $"<p>{Encode(f.ContactLine)}</p>",
            ["guest.name"] = Encode(string.IsNullOrWhiteSpace(f.GuestName) ? "there" : f.GuestName),
            ["business.name"] = Encode(f.BusinessName),
            ["business.url"] = f.BusinessUrl is { Length: > 0 } bu
                ? $"<a href=\"{Encode(bu)}\">{Encode(f.BusinessName)}</a>" : Encode(f.BusinessName),
            ["business.contact"] = Encode(f.ContactLine ?? string.Empty),
            ["site.name"] = Encode(f.SiteName),
        };
    }

    /// <summary>Unknown placeholders render as nothing.</summary>
    /// <remarks>
    /// Rather than leaving <c>{{tour.nmae}}</c> in the mail a guest reads. The editor lists what
    /// works, and a typo should look like an omission rather than like machinery showing through.
    /// </remarks>
    private static string Substitute(string template, Dictionary<string, string> values)
        => Placeholder.Replace(template, m =>
            values.TryGetValue(m.Groups[1].Value, out var value) ? value : string.Empty);

    /// <summary>
    /// The guides' faces, as Ben asked for them: optional, and per date.
    /// </summary>
    /// <remarks>
    /// Inline styles, because mail clients drop stylesheets. Alt text carries the name, so a guest
    /// whose mail client blocks images — most of them, by default — still reads who to look for.
    /// </remarks>
    private static string Photos(IReadOnlyList<(string Name, string PhotoUrl)> photos)
    {
        if (photos.Count == 0) return string.Empty;

        var html = new StringBuilder("<p>");
        foreach (var (name, url) in photos)
        {
            html.Append($"<img src=\"{Encode(url)}\" alt=\"{Encode(name)}\" width=\"96\" height=\"96\" ")
                .Append("style=\"border-radius:48px;object-fit:cover;margin-right:8px;\" />");
        }
        return html.Append("</p>").ToString();
    }

    /// <summary>"Jane", "Jane and Marcus", "Jane, Marcus and Ada".</summary>
    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "your guide",
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

    private static string Length(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours switch
        {
            0 => $"about {rest} minutes",
            _ when rest == 0 => $"about {hours} hour{(hours == 1 ? "" : "s")}",
            _ => $"about {hours} hour{(hours == 1 ? "" : "s")} {rest} minutes",
        };
    }

    /// <summary>"Central Daylight Time" → "CDT"; anything unrecognisable is left alone.</summary>
    private static string Short(string zoneName)
    {
        var words = zoneName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2
            ? new string([.. words.Select(w => w[0])])
            : zoneName;
    }

    /// <summary>The tour's zone, or the server's when the id is not one this machine knows.</summary>
    /// <remarks>
    /// Never throws. A mail that goes out with a time in the wrong zone is bad; a mail that does
    /// not go out at all because a zone database is missing an entry is worse.
    /// </remarks>
    internal static TimeZoneInfo ZoneOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
