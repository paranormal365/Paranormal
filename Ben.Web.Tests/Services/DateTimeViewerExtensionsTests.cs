using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

public class DateTimeViewerExtensionsTests
{
    private sealed class FakeUserState(TimeZoneInfo timeZone) : IBenUserState
    {
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public bool IsModerator => false;
        public bool IsAdmin => false;
        public bool IsImpersonating => false;
        public string? UserEmail => null;
        public Guid? UserId => null;
        public Task AuthReady => Task.CompletedTask;
        public TimeZoneInfo BrowserTimeZone { get; } = timeZone;
        public event Action? StateChanged { add { } remove { } }
    }

    private static IBenUserState UserStateFor(string ianaId) =>
        new FakeUserState(TimeZoneInfo.FindSystemTimeZoneById(ianaId));

    public static IEnumerable<object[]> IanaZones =>
        [
            ["America/Chicago"],
            ["America/New_York"],
            ["Asia/Kolkata"],       // UTC+5:30 — non-hour-aligned offset
            ["Pacific/Kiritimati"], // UTC+14 — large positive offset
            ["UTC"],
        ];

    [Theory]
    [MemberData(nameof(IanaZones))]
    public void ToViewerLocalTime_ThenBack_RoundTripsToSameUtcInstant(string ianaId)
    {
        var userState = UserStateFor(ianaId);
        var utc = new DateTime(2026, 3, 15, 18, 30, 0, DateTimeKind.Utc);

        var local = utc.ToViewerLocalTime(userState);
        var backToUtc = local.ToUtcFromViewerLocal(userState);

        Assert.Equal(utc, backToUtc);
    }

    [Fact]
    public void ToViewerLocalTime_ForUtcViewer_IsIdentity()
    {
        var userState = UserStateFor("UTC");
        var utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        var local = utc.ToViewerLocalTime(userState);

        Assert.Equal(utc, local);
    }

    [Fact]
    public void ToViewerLocalTime_ForChicagoViewer_ConvertsToCorrectOffset()
    {
        var userState = UserStateFor("America/Chicago");
        // 2026-07-15 is during CDT (UTC-5)
        var utc = new DateTime(2026, 7, 15, 18, 0, 0, DateTimeKind.Utc);

        var local = utc.ToViewerLocalTime(userState);

        Assert.Equal(new DateTime(2026, 7, 15, 13, 0, 0), local);
    }

    [Fact]
    public void ToViewerLocalTime_AcceptsUnspecifiedKind_MatchingEfCoreReadShape()
    {
        var userState = UserStateFor("America/Chicago");
        var unspecified = new DateTime(2026, 7, 15, 18, 0, 0, DateTimeKind.Unspecified);

        var local = unspecified.ToViewerLocalTime(userState);

        Assert.Equal(new DateTime(2026, 7, 15, 13, 0, 0), local);
    }

    [Fact]
    public void ToUtcFromViewerLocal_SpringForwardGap_DoesNotThrow()
    {
        var userState = UserStateFor("America/Chicago");
        // 2026-03-08 02:30 local does not exist (clocks spring forward 2:00 -> 3:00 CDT)
        var nonexistentLocal = new DateTime(2026, 3, 8, 2, 30, 0);

        var utc = nonexistentLocal.ToUtcFromViewerLocal(userState);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void ToUtcFromViewerLocal_FallBackOverlap_DoesNotThrow()
    {
        var userState = UserStateFor("America/Chicago");
        // 2026-11-01 01:30 local occurs twice (clocks fall back 2:00 CDT -> 1:00 CST)
        var ambiguousLocal = new DateTime(2026, 11, 1, 1, 30, 0);

        var utc = ambiguousLocal.ToUtcFromViewerLocal(userState);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void NowInViewerTimeZone_IsCloseToUtcNowConvertedTheSameWay()
    {
        var userState = UserStateFor("America/Chicago");

        var result = userState.NowInViewerTimeZone();
        var expected = DateTime.UtcNow.ToViewerLocalTime(userState);

        Assert.True((result - expected).Duration() < TimeSpan.FromSeconds(5));
    }

    // ── ToDisplaySpan ────────────────────────────────────────────────────────
    //
    // Ben's rule, 2026-09-09: "you usually arrive like 3pm on one day and the investigation ends
    // 8am the next day, this is considered a single-day investigation". So crossing midnight is
    // not a multi-day visit, and the reading has to say which one it is.

    [Fact]
    public void ToDisplaySpan_SameEvening_ShowsOnlyTheEndTime()
    {
        var start = new DateTime(2026, 9, 14, 19, 0, 0);
        var end   = new DateTime(2026, 9, 14, 23, 30, 0);

        Assert.Equal("09/14/2026 07:00 PM – 11:30 PM", start.ToDisplaySpan(end));
    }

    [Fact]
    public void ToDisplaySpan_PastMidnight_SaysNextDay()
    {
        var start = new DateTime(2026, 9, 14, 15, 0, 0);
        var end   = new DateTime(2026, 9, 15, 8, 0, 0);

        Assert.Equal("09/14/2026 03:00 PM – 08:00 AM next day", start.ToDisplaySpan(end));
    }

    [Fact]
    public void ToDisplaySpan_AcrossAWeek_ShowsBothDates()
    {
        var start = new DateTime(2026, 9, 14, 15, 0, 0);
        var end   = new DateTime(2026, 9, 21, 8, 0, 0);

        Assert.Equal("09/14/2026 03:00 PM – 09/21/2026 08:00 AM", start.ToDisplaySpan(end));
    }

    [Fact]
    public void ToDisplaySpan_NoEnd_IsJustTheStart()
    {
        var start = new DateTime(2026, 9, 14, 15, 0, 0);

        Assert.Equal("09/14/2026 03:00 PM", start.ToDisplaySpan(null));
    }

    [Fact]
    public void ToDisplaySpan_EndBeforeStart_IsIgnoredRatherThanPrinted()
    {
        // Rows written before the server refused this shape. A reading that says
        // "3pm – 2pm" invites somebody to fix the wrong thing.
        var start = new DateTime(2026, 9, 14, 15, 0, 0);
        var end   = new DateTime(2026, 9, 14, 14, 0, 0);

        Assert.Equal("09/14/2026 03:00 PM", start.ToDisplaySpan(end));
    }
}
