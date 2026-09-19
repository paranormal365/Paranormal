using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What counts as a Microsoft visit.
/// </summary>
/// <remarks>
/// Ben, 2026-09-19: "not needed every time they change a page... more like true visits as close as
/// possible." The window is the whole of that decision, so it is worth being able to read it in
/// one place and test it without a clock.
/// </remarks>
public sealed class EntraSignInSessionsTests
{
    private static readonly DateTime Noon = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Somebody_who_has_never_arrived_is_arriving()
        => Assert.True(EntraSignInSessions.IsANewVisit(null, Noon));

    [Fact]
    public void The_next_request_of_the_same_visit_is_not_an_arrival()
        => Assert.False(EntraSignInSessions.IsANewVisit(Noon, Noon.AddSeconds(1)));

    /// <summary>
    /// The case the whole rule exists for: a token arriving on every page change, all afternoon.
    /// </summary>
    [Fact]
    public void An_afternoon_of_requests_is_one_visit()
    {
        foreach (var hours in new[] { 0.5, 1, 2, 4, 8, 11.9 })
            Assert.False(EntraSignInSessions.IsANewVisit(Noon, Noon.AddHours(hours)),
                $"{hours}h after the last arrival still reads as a new one.");
    }

    [Fact]
    public void Coming_back_in_the_evening_is_a_second_visit()
        => Assert.True(EntraSignInSessions.IsANewVisit(Noon, Noon.Add(EntraSignInSessions.Visit)));

    [Fact]
    public void And_the_next_day_certainly_is()
        => Assert.True(EntraSignInSessions.IsANewVisit(Noon, Noon.AddDays(1)));

    /// <summary>
    /// Twelve hours, named rather than assumed: a working day is one visit and an evening is
    /// another, which is what makes the count comparable with a password sign-in's.
    /// </summary>
    [Fact]
    public void A_visit_is_twelve_hours()
        => Assert.Equal(TimeSpan.FromHours(12), EntraSignInSessions.Visit);
}
