using Ben.Data.Source.Entities;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A guest who arrives a few minutes late can still sign up (Ben, 2026-08-27).
/// </summary>
/// <remarks>
/// <para>Sign-ups used to stop dead on the start time. For a ghost walking tour that is the wrong
/// rule: a guest reaches the meeting point after the guide has set off, sometimes with friends,
/// and is waved in. The guide takes their money, and the site behaved as though they were never
/// there — which also cost them the photograph they took on the walk, because evidence submission
/// is gated on attendance.</para>
///
/// <para>The window is bounded rather than open, because the closing rule is ALSO what stops
/// somebody signing up to last week's event to reach the evidence submitted to it.</para>
/// </remarks>
public class LateSignUpGraceTests
{
    private static OrgCalendarEvent At(DateTime start, DateTime? closes = null) =>
        new() { StartDateTime = start, RsvpClosesAt = closes };

    [Fact]
    public void Sign_ups_stay_open_for_a_guest_who_is_a_few_minutes_late()
    {
        var startedTenMinutesAgo = At(DateTime.UtcNow.AddMinutes(-10));

        Assert.True(DateTime.UtcNow < startedTenMinutesAgo.RsvpClosingTime);
    }

    [Fact]
    public void And_close_once_the_grace_has_run_out()
    {
        var startedAnHourAgo = At(DateTime.UtcNow.AddHours(-1));

        Assert.True(DateTime.UtcNow > startedAnHourAgo.RsvpClosingTime);
    }

    /// <summary>Last week's tour stays shut — the protection the grace must not undo.</summary>
    [Fact]
    public void A_long_finished_event_cannot_be_joined()
    {
        var lastWeek = At(DateTime.UtcNow.AddDays(-7));

        Assert.True(DateTime.UtcNow > lastWeek.RsvpClosingTime);
    }

    /// <summary>
    /// An organiser's explicit closing time wins, in BOTH directions.
    /// </summary>
    /// <remarks>
    /// Closing early is a real need — a tour that must be booked an hour ahead — and staying open
    /// longer is the operator choosing a wider grace than the default for a particular night.
    /// </remarks>
    [Fact]
    public void An_explicit_closing_time_overrides_the_grace()
    {
        var closedEarly = At(DateTime.UtcNow.AddMinutes(-5), closes: DateTime.UtcNow.AddMinutes(-30));
        Assert.True(DateTime.UtcNow > closedEarly.RsvpClosingTime);

        var heldOpen = At(DateTime.UtcNow.AddHours(-3), closes: DateTime.UtcNow.AddHours(1));
        Assert.True(DateTime.UtcNow < heldOpen.RsvpClosingTime);
    }

    /// <summary>
    /// The grace is for GUESTS signing themselves up. A guide adding someone has no time limit.
    /// </summary>
    /// <remarks>
    /// Two mechanisms, deliberately: the public window is bounded because nobody vouches for a
    /// stranger with a phone, while a guide adding a walk-up is present and accountable, so it is
    /// gated on the calendar permission rather than on the clock. The walk-up with three friends
    /// and no account is the case the grace alone cannot serve.
    /// </remarks>
    [Fact]
    public void The_public_grace_is_bounded_but_a_guide_is_not_bound_by_it()
    {
        var lastNight = At(DateTime.UtcNow.AddHours(-14));

        // Self-service is shut …
        Assert.True(DateTime.UtcNow > lastNight.RsvpClosingTime);
        // … and nothing in the closing rule constrains the organiser path, which answers to the
        // calendar grant in OrgCalendarEventController.AddAttendee/AddAttendeeByEmail.
    }

    /// <summary>The default is half an hour, stated so a change is a decision.</summary>
    [Fact]
    public void The_grace_is_thirty_minutes()
        => Assert.Equal(TimeSpan.FromMinutes(30), OrgCalendarEvent.LateSignUpGrace);
}
