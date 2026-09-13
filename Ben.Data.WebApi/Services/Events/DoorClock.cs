namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// When somebody came in, as the door records it (item 235 phase 14c).
/// </summary>
/// <remarks>
/// <para><b>Why a phone may say.</b> The phone's door keeps an arrival it could not send — a cellar, a
/// hotel with one bar — and sends it when it finds a signal. Recorded at that moment, a party that came
/// in at nine would read as arriving at eleven, and "who was in the building at ten" would be wrong.</para>
///
/// <para><b>Why the server still decides.</b> A time in the future is a phone with a wrong clock, and a
/// time days before the night is nothing a door could have seen; both are taken as now, so a device can
/// never post-date an arrival.</para>
/// </remarks>
public static class DoorClock
{
    /// <param name="told">What the door said, if anything.</param>
    /// <param name="now">The server's now, UTC.</param>
    /// <param name="nightDate">The night's calendar date. An arrival may be the evening before in UTC,
    /// never earlier.</param>
    public static DateTime ArrivedAt(DateTime? told, DateTime now, DateTime nightDate)
    {
        if (told is not { } when) return now;

        var utc = when.Kind == DateTimeKind.Local ? when.ToUniversalTime() : DateTime.SpecifyKind(when, DateTimeKind.Utc);
        if (utc > now) return now;
        if (utc < nightDate.Date.AddDays(-1)) return now;
        return utc;
    }
}
