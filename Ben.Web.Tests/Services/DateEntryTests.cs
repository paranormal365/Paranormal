using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 224: a typed date that cannot exist is refused with a sentence, never turned into another date.
/// </summary>
/// <remarks>
/// The cases are the ones measured on the Telerik pickers on 2026-09-15: 09/31 became 09/01 and hour 13 with PM
/// became 03 PM, silently. Each of those must be a refusal here.
/// </remarks>
public sealed class DateEntryTests
{
    private static readonly DateTime Seeded = new(2026, 9, 15, 20, 30, 0);

    [Fact]
    public void The_thirty_first_of_September_is_refused_and_says_how_many_days_the_month_has()
    {
        var r = DateEntry.Parse("09/31/2026", DateEntryMode.Date, Seeded);
        Assert.Null(r.Value);
        Assert.Equal("September 2026 has 30 days, so 09/31/2026 isn't a date.", r.Error);
    }

    [Fact]
    public void The_thirty_first_of_October_is_a_date_and_keeps_the_time_already_there()
    {
        var r = DateEntry.Parse("10/31/2026", DateEntryMode.Date, Seeded);
        Assert.Null(r.Error);
        Assert.Equal(new DateTime(2026, 10, 31, 20, 30, 0), r.Value);
    }

    [Theory]
    [InlineData("02/29/2028", true)]
    [InlineData("02/29/2027", false)]
    public void February_29_exists_only_in_a_leap_year(string typed, bool exists)
    {
        var r = DateEntry.Parse(typed, DateEntryMode.Date, null);
        Assert.Equal(exists, r.Error is null);
        if (!exists) Assert.Equal("February 2027 has 28 days, so 02/29/2027 isn't a date.", r.Error);
    }

    [Theory]
    [InlineData("9/3/26")]
    [InlineData("09-03-2026")]
    [InlineData("2026-09-03")]
    [InlineData(" 09/03/2026 ")]
    public void Common_ways_of_writing_a_date_are_read(string typed)
        => Assert.Equal(new DateTime(2026, 9, 3), DateEntry.Parse(typed, DateEntryMode.Date, null).Value);

    [Theory]
    [InlineData("13/01/2026", "There is no month 13 — months run from 1 to 12.")]
    [InlineData("09/00/2026", "Days start at 1.")]
    [InlineData("09/15/226", "Write the year in full, like 2026.")]
    [InlineData("tomorrow", "Write the date as MM/DD/YYYY, like 09/15/2026.")]
    public void Other_impossible_dates_each_say_what_is_wrong(string typed, string sentence)
        => Assert.Equal(sentence, DateEntry.Parse(typed, DateEntryMode.Date, null).Error);

    [Fact]
    public void Hour_thirteen_with_PM_is_refused_not_turned_into_three()
    {
        var r = DateEntry.Parse("13:00 PM", DateEntryMode.Time, Seeded);
        Assert.Null(r.Value);
        Assert.Equal("13 PM isn't a time — use 1 to 12 with AM or PM, or 13:00 on a 24-hour clock.", r.Error);
    }

    [Theory]
    [InlineData("08:00 PM", 20, 0)]
    [InlineData("8pm", 20, 0)]
    [InlineData("8:30 p.m.", 20, 30)]
    [InlineData("830pm", 20, 30)]
    [InlineData("20:00", 20, 0)]
    [InlineData("13", 13, 0)]
    [InlineData("12:15 AM", 0, 15)]
    [InlineData("12 pm", 12, 0)]
    public void Times_are_read_on_either_clock_and_keep_the_day_already_there(string typed, int hour, int minute)
        => Assert.Equal(new DateTime(2026, 9, 15, hour, minute, 0), DateEntry.Parse(typed, DateEntryMode.Time, Seeded).Value);

    [Theory]
    [InlineData("25:00", "There is no hour 25 — hours run from 0 to 23, or 1 to 12 with AM or PM.")]
    [InlineData("08:61 PM", "There is no minute 61 — minutes run from 00 to 59.")]
    public void Impossible_times_each_say_what_is_wrong(string typed, string sentence)
        => Assert.Equal(sentence, DateEntry.Parse(typed, DateEntryMode.Time, Seeded).Error);

    [Fact]
    public void A_date_and_time_reads_both_halves()
        => Assert.Equal(new DateTime(2026, 10, 3, 19, 45, 0), DateEntry.Parse("10/03/2026 7:45 PM", DateEntryMode.DateTime, null).Value);

    [Fact]
    public void A_date_and_time_with_an_impossible_day_is_refused()
        => Assert.Equal("September 2026 has 30 days, so 09/31/2026 isn't a date.",
                        DateEntry.Parse("09/31/2026 08:00 PM", DateEntryMode.DateTime, Seeded).Error);

    [Fact]
    public void A_date_alone_in_a_date_and_time_field_keeps_the_time_there_or_asks_for_one()
    {
        Assert.Equal(new DateTime(2026, 10, 1, 20, 30, 0), DateEntry.Parse("10/01/2026", DateEntryMode.DateTime, Seeded).Value);
        Assert.Equal("Add a time after the date, like 10/01/2026 08:00 PM.", DateEntry.Parse("10/01/2026", DateEntryMode.DateTime, null).Error);
    }

    [Fact]
    public void Blank_is_empty_not_an_error_the_field_decides_whether_blank_is_allowed()
        => Assert.Equal(DateEntryResult.Empty, DateEntry.Parse("   ", DateEntryMode.Date, Seeded));

    [Theory]
    [InlineData(DateEntryMode.Date, "09/15/2026")]
    [InlineData(DateEntryMode.DateTime, "09/15/2026 08:30 PM")]
    [InlineData(DateEntryMode.Time, "08:30 PM")]
    public void What_a_field_shows_reads_back_as_the_same_value(DateEntryMode mode, string shown)
    {
        Assert.Equal(shown, DateEntry.Format(Seeded, mode));
        Assert.Null(DateEntry.Parse(shown, mode, Seeded).Error);
    }
}
