using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A time somebody types is on their own clock; it is stored as UTC (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Ben scheduled an investigation from Central for 06:00 AM and the list read 07:00 AM CDT.
/// The window converted with <c>ToUniversalTime()</c>, which on a Blazor Server page uses the
/// SERVER's zone — a fixed UTC-6 on production — not the reader's. These pin the conversion the
/// forms now use, and none of them depends on the zone of the machine running the tests.</para>
/// </remarks>
public class ViewerTimeZoneSaveTests
{
    private sealed class FakeUserState(TimeZoneInfo timeZone) : IBenUserState
    {
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public bool IsModerator => false;
        public bool IsSeller => false;
        public bool IsAdmin => false;
        public bool IsImpersonating => false;
        public string? UserEmail => null;
        public Guid? UserId => null;
        public Task AuthReady => Task.CompletedTask;
        public TimeZoneInfo BrowserTimeZone { get; } = timeZone;
        public event Action? StateChanged { add { } remove { } }
    }

    private static readonly IBenUserState Central =
        new FakeUserState(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));

    [Fact]
    public void The_reported_multi_day_visit_saves_on_the_readers_clock()
    {
        // 10/16/2026 06:00 AM to 10/30/2026 11:00 PM, typed in Central (CDT, UTC-5).
        var (start, end) = InvestigationWindow.ToUtc(
            new DateTime(2026, 10, 16, 6, 0, 0), new DateTime(2026, 10, 30, 23, 0, 0), Central);

        Assert.Equal(new DateTime(2026, 10, 16, 11, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 31, 4, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    public void The_same_window_reads_back_as_typed()
    {
        var typedStart = new DateTime(2026, 10, 16, 6, 0, 0);
        var typedEnd = new DateTime(2026, 10, 30, 23, 0, 0);

        var (start, end) = InvestigationWindow.ToUtc(typedStart, typedEnd, Central);

        Assert.Equal(typedStart, start.ToViewerLocalTime(Central));
        Assert.Equal(typedEnd, end!.Value.ToViewerLocalTime(Central));
    }

    /// <summary>
    /// 7 PM to 7 AM across the fall-back night is thirteen hours, not twelve.
    /// </summary>
    /// <remarks>
    /// 11/01/2026 is the day clocks go back in Central. The evening is CDT (UTC-5) and the morning
    /// is CST (UTC-6), so each end has to be converted on its own clock. Converting the start and
    /// adding twelve hours would put the end at 6 AM.
    /// </remarks>
    [Fact]
    public void An_overnight_visit_across_the_fall_back_change_is_thirteen_hours()
    {
        var start = new DateTime(2026, 10, 31, 19, 0, 0);
        var end = InvestigationWindow.SingleDayEnd(start, new TimeSpan(7, 0, 0));
        Assert.Equal(new DateTime(2026, 11, 1, 7, 0, 0), end);

        var (startUtc, endUtc) = InvestigationWindow.ToUtc(start, end, Central);

        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 13, 0, 0, DateTimeKind.Utc), endUtc);
        Assert.Equal(TimeSpan.FromHours(13), endUtc!.Value - startUtc);
    }

    [Fact]
    public void An_overnight_visit_across_the_spring_forward_change_is_eleven_hours()
    {
        // 03/14/2027: Central goes from CST to CDT at 2 AM.
        var start = new DateTime(2027, 3, 13, 19, 0, 0);
        var end = InvestigationWindow.SingleDayEnd(start, new TimeSpan(7, 0, 0));

        var (startUtc, endUtc) = InvestigationWindow.ToUtc(start, end, Central);

        Assert.Equal(TimeSpan.FromHours(11), endUtc!.Value - startUtc);
    }

    [Fact]
    public void No_end_stays_no_end()
    {
        var (_, end) = InvestigationWindow.ToUtc(new DateTime(2026, 10, 16, 6, 0, 0), null, Central);
        Assert.Null(end);
    }

    [Fact]
    public void A_viewer_elsewhere_saves_on_their_own_clock()
    {
        var pacific = new FakeUserState(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));

        var (start, _) = InvestigationWindow.ToUtc(new DateTime(2026, 10, 16, 6, 0, 0), null, pacific);

        Assert.Equal(new DateTime(2026, 10, 16, 13, 0, 0, DateTimeKind.Utc), start);
    }

    [Fact]
    public void A_bare_time_in_an_empty_field_lands_on_the_day_the_caller_names()
    {
        var result = DateEntry.Parse("7:00 PM", DateEntryMode.Time, current: null, today: new DateTime(2026, 10, 16));
        Assert.Equal(new DateTime(2026, 10, 16, 19, 0, 0), result.Value);
    }

    [Fact]
    public void A_bare_time_keeps_the_fields_own_day_over_today()
    {
        var result = DateEntry.Parse("7:00 PM", DateEntryMode.Time,
            current: new DateTime(2026, 11, 20, 8, 0, 0), today: new DateTime(2026, 10, 16));
        Assert.Equal(new DateTime(2026, 11, 20, 19, 0, 0), result.Value);
    }
}
