using System.Reflection;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Covers the calendar's recurrence presets — the RRULE each one produces, and the round trip back
/// from a stored rule to the preset that made it.
/// </summary>
/// <remarks>
/// Reached by reflection because the logic lives in a .razor component's @code block, which has no
/// public surface. Worth testing anyway: an RRULE that is subtly wrong creates a real series of
/// events on the wrong days, and nobody reads RFC5545 closely enough to catch it by eye.
/// </remarks>
public sealed class RecurrencePresetTests
{
    // Anchored on the marker class rather than any service interface: the service layer lives in
    // its own assembly (Ben.Web.Services), so only a type that cannot leave the component library
    // identifies the assembly the components are actually in.
    private static readonly Type Scheduler =
        typeof(Ben.Web.Website.Library.LibraryAssemblyMarker).Assembly
            .GetType("Ben.Web.Website.Library.Manage.Calendar.OrgScheduler")!;

    private static string? RuleFor(string choice, DateTime start)
        => (string?)Scheduler
            .GetMethod("RecurrenceRuleFor", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [choice, start]);

    private static string ChoiceFor(string? rule, DateTime start)
        => (string)Scheduler
            .GetMethod("RecurrenceChoiceFor", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [rule, start])!;

    // A Tuesday, deliberately: several rules encode the start's own weekday.
    private static readonly DateTime Tuesday = new(2026, 8, 11, 19, 30, 0);

    [Fact]
    public void The_component_and_its_helpers_are_reachable()
    {
        // Guards the reflection above: a rename would otherwise turn every test here green-by-absence.
        Assert.NotNull(Scheduler);
    }

    [Theory]
    [InlineData("daily",    "FREQ=DAILY")]
    [InlineData("weekdays", "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR")]
    [InlineData("weekly",   "FREQ=WEEKLY;BYDAY=TU")]
    [InlineData("biweekly", "FREQ=WEEKLY;INTERVAL=2;BYDAY=TU")]
    [InlineData("monthly",  "FREQ=MONTHLY;BYMONTHDAY=11")]
    [InlineData("yearly",   "FREQ=YEARLY;BYMONTH=8;BYMONTHDAY=11")]
    public void Each_preset_produces_its_rule(string choice, string expected)
        => Assert.Equal(expected, RuleFor(choice, Tuesday));

    [Fact]
    public void Does_not_repeat_produces_no_rule()
    {
        Assert.Null(RuleFor("none", Tuesday));
        Assert.Null(RuleFor("custom", Tuesday));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday,    "SU")]
    [InlineData(DayOfWeek.Monday,    "MO")]
    [InlineData(DayOfWeek.Wednesday, "WE")]
    [InlineData(DayOfWeek.Saturday,  "SA")]
    public void Weekly_follows_the_day_the_event_starts(DayOfWeek day, string ical)
    {
        // 2026-08-09 is a Sunday; walk forward to the day under test.
        var start = new DateTime(2026, 8, 9).AddDays((int)day);
        Assert.Equal(day, start.DayOfWeek);

        Assert.Equal($"FREQ=WEEKLY;BYDAY={ical}", RuleFor("weekly", start));
    }

    // ── Round trip ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("daily")]
    [InlineData("weekdays")]
    [InlineData("weekly")]
    [InlineData("biweekly")]
    [InlineData("monthly")]
    [InlineData("yearly")]
    public void A_rule_resolves_back_to_the_preset_that_made_it(string choice)
        => Assert.Equal(choice, ChoiceFor(RuleFor(choice, Tuesday), Tuesday));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_rule_resolves_to_does_not_repeat(string? rule)
        => Assert.Equal("none", ChoiceFor(rule, Tuesday));

    [Fact]
    public void An_unrecognised_rule_resolves_to_custom_rather_than_being_rewritten()
    {
        // The important half: a hand-written rule must stay visible and editable, not be silently
        // flattened into whichever preset looks closest.
        Assert.Equal("custom", ChoiceFor("FREQ=MONTHLY;BYDAY=3TU", Tuesday));
        Assert.Equal("custom", ChoiceFor("FREQ=DAILY;INTERVAL=3;COUNT=10", Tuesday));
    }

    [Fact]
    public void Matching_ignores_case_and_surrounding_space()
        => Assert.Equal("daily", ChoiceFor("  freq=daily  ", Tuesday));

    [Fact]
    public void The_same_rule_means_different_presets_on_different_start_days()
    {
        // "Every week on Tuesday" is only that rule for an event starting on a Tuesday. Read
        // against a Wednesday start it is no longer the weekly preset, and must fall to custom
        // rather than quietly claiming to be "every week on Wednesday".
        var wednesday = Tuesday.AddDays(1);

        Assert.Equal("weekly", ChoiceFor("FREQ=WEEKLY;BYDAY=TU", Tuesday));
        Assert.Equal("custom", ChoiceFor("FREQ=WEEKLY;BYDAY=TU", wednesday));
    }
}
