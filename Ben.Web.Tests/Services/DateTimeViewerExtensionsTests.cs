using Ben.Web.Library.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

public class DateTimeViewerExtensionsTests
{
    private sealed class FakeUserState(TimeZoneInfo timeZone) : IBenUserState
    {
        public bool IsAuthenticated => true;
        public bool IsSuperAdmin => false;
        public bool IsImpersonating => false;
        public string? UserEmail => null;
        public Guid? UserId => null;
        public Task AuthReady => Task.CompletedTask;
        public TimeZoneInfo BrowserTimeZone { get; } = timeZone;
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
}
