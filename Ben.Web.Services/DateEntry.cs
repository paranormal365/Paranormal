using System.Globalization;
using System.Text.RegularExpressions;

namespace Ben.Web.Services;

/// <summary>What a typed date field holds.</summary>
public enum DateEntryMode
{
    /// <summary><c>MM/dd/yyyy</c>. The time of day is kept from the field's current value.</summary>
    Date,
    /// <summary><c>MM/dd/yyyy hh:mm tt</c>.</summary>
    DateTime,
    /// <summary><c>hh:mm tt</c>. The day is kept from the field's current value.</summary>
    Time,
}

/// <summary>A typed date read back: the value, or the sentence saying why the text is not one.</summary>
public readonly record struct DateEntryResult(DateTime? Value, string? Error)
{
    public static DateEntryResult Empty => new(null, null);
    public static DateEntryResult Fail(string error) => new(null, error);
}

/// <summary>
/// Reads what somebody typed into a date, date-and-time or time field (item 224).
/// </summary>
/// <remarks>
/// <para><b>Why the site parses dates itself.</b> Telerik's segmented date input changes an impossible day instead of
/// refusing it: typing 0-9-3-1 gives 09/01, and 1-3 in a 12-hour hour gives 03 PM, with nothing on screen. Measured
/// 2026-09-15 on 14.1 and 15.0.1 alike; its <c>AutoCorrectParts</c> switch only swaps that for a red field whose
/// value never saves, and the browser's own date input did worse. So the text is read here, and every refusal is a
/// sentence naming what was wrong — "September 2026 has 30 days" — instead of a different date.</para>
///
/// <para>Lenient about shape (<c>9/3/26</c>, <c>2026-09-03</c>, <c>8pm</c>, <c>20:00</c>), strict about meaning: a
/// part that cannot exist is never turned into one that can.</para>
/// </remarks>
public static partial class DateEntry
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Format(DateTime value, DateEntryMode mode) => mode switch
    {
        DateEntryMode.Date     => value.ToString(DateTimeViewerExtensions.DatePattern, Invariant),
        DateEntryMode.DateTime => value.ToString(DateTimeViewerExtensions.DateTimePattern, Invariant),
        _                      => value.ToString(DateTimeViewerExtensions.TimePattern, Invariant),
    };

    public static string Example(DateEntryMode mode) => mode switch
    {
        DateEntryMode.Date     => "MM/DD/YYYY",
        DateEntryMode.DateTime => "MM/DD/YYYY 08:00 PM",
        _                      => "08:00 PM",
    };

    /// <summary>
    /// Reads <paramref name="text"/>. Blank text is <see cref="DateEntryResult.Empty"/>; whether blank is allowed is
    /// the field's decision. <paramref name="current"/> supplies the half a mode does not carry.
    /// </summary>
    public static DateEntryResult Parse(string? text, DateEntryMode mode, DateTime? current)
    {
        var typed = (text ?? string.Empty).Trim();
        if (typed.Length == 0) return DateEntryResult.Empty;

        switch (mode)
        {
            case DateEntryMode.Date:
            {
                var (date, error) = ParseDate(typed);
                return error is not null
                    ? DateEntryResult.Fail(error)
                    : new DateEntryResult(date!.Value.Date + (current?.TimeOfDay ?? TimeSpan.Zero), null);
            }
            case DateEntryMode.Time:
            {
                var (time, error) = ParseTime(typed);
                return error is not null
                    ? DateEntryResult.Fail(error)
                    : new DateEntryResult((current?.Date ?? System.DateTime.Today) + time!.Value, null);
            }
            default:
            {
                var space = typed.IndexOf(' ');
                var datePart = space < 0 ? typed : typed[..space];
                var timePart = space < 0 ? string.Empty : typed[(space + 1)..].Trim();

                var (date, dateError) = ParseDate(datePart);
                if (dateError is not null) return DateEntryResult.Fail(dateError);

                if (timePart.Length == 0)
                {
                    // A date on its own keeps the time already there; with nothing there, a guessed midnight would be
                    // a time nobody chose.
                    return current is { } now
                        ? new DateEntryResult(date!.Value.Date + now.TimeOfDay, null)
                        : DateEntryResult.Fail($"Add a time after the date, like {date!.Value.ToString(DateTimeViewerExtensions.DatePattern, Invariant)} 08:00 PM.");
                }

                var (time, timeError) = ParseTime(timePart);
                return timeError is not null
                    ? DateEntryResult.Fail(timeError)
                    : new DateEntryResult(date!.Value.Date + time!.Value, null);
            }
        }
    }

    private static (DateTime? Date, string? Error) ParseDate(string typed)
    {
        var m = DateShape().Match(typed);
        if (!m.Success) return (null, "Write the date as MM/DD/YYYY, like 09/15/2026.");

        string a = m.Groups[1].Value, b = m.Groups[2].Value, c = m.Groups[3].Value;
        int year, month, day;
        if (a.Length == 4)
        {
            if (b.Length > 2 || c.Length > 2) return (null, "Write the date as MM/DD/YYYY, like 09/15/2026.");
            (year, month, day) = (int.Parse(a, Invariant), int.Parse(b, Invariant), int.Parse(c, Invariant));
        }
        else
        {
            if (b.Length > 2 || a.Length > 2) return (null, "Write the date as MM/DD/YYYY, like 09/15/2026.");
            (month, day) = (int.Parse(a, Invariant), int.Parse(b, Invariant));
            year = c.Length switch
            {
                2 => 2000 + int.Parse(c, Invariant),
                4 => int.Parse(c, Invariant),
                _ => -1,
            };
            if (year < 0) return (null, "Write the year in full, like 2026.");
        }

        if (month is < 1 or > 12) return (null, $"There is no month {month} — months run from 1 to 12.");
        if (year is < 1900 or > 2199) return (null, $"{year} is outside the years this site takes.");

        var days = System.DateTime.DaysInMonth(year, month);
        if (day < 1 || day > days)
        {
            var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", Invariant);
            return (null, day < 1
                ? "Days start at 1."
                : $"{monthName} has {days} days, so {month:00}/{day:00}/{year} isn't a date.");
        }

        return (new DateTime(year, month, day), null);
    }

    private static (TimeSpan? Time, string? Error) ParseTime(string typed)
    {
        var compact = typed.ToLowerInvariant().Replace(" ", string.Empty).Replace(".", string.Empty);
        var m = TimeShape().Match(compact);
        if (!m.Success) return (null, "Write the time like 08:00 PM or 20:00.");

        var hour = int.Parse(m.Groups[1].Value, Invariant);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, Invariant) : 0;
        var half = m.Groups[3].Success ? m.Groups[3].Value : null;

        if (minute > 59) return (null, $"There is no minute {minute} — minutes run from 00 to 59.");

        if (half is not null)
        {
            var pm = half.StartsWith('p');
            if (hour is < 1 or > 12)
                return (null, $"{hour} {(pm ? "PM" : "AM")} isn't a time — use 1 to 12 with AM or PM, or {Math.Clamp(hour, 0, 23):00}:{minute:00} on a 24-hour clock.");
            hour = hour % 12 + (pm ? 12 : 0);
        }
        else if (hour > 23)
        {
            return (null, $"There is no hour {hour} — hours run from 0 to 23, or 1 to 12 with AM or PM.");
        }

        return (new TimeSpan(hour, minute, 0), null);
    }

    [GeneratedRegex(@"^(\d{1,4})[/\-.](\d{1,2})[/\-.](\d{1,4})$")]
    private static partial Regex DateShape();

    [GeneratedRegex(@"^(\d{1,2})(?::?(\d{2}))?(am|pm|a|p)?$")]
    private static partial Regex TimeShape();
}
