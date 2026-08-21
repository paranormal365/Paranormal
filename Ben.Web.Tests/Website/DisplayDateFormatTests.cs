using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Pins the site's date formats. These moved once already (month-first to day-first), and the
/// failure mode is silent: 04/08 is a valid date either way, so a drifted format reads as a
/// different day rather than as something broken.
/// </summary>
public class DisplayDateFormatTests
{
    // 4 August 2026, 9:30:05 PM — the day and month differ, so a swap is visible.
    private static readonly DateTime Sample = new(2026, 8, 4, 21, 30, 5);

    [Fact]
    public void Date_IsMonthFirst() => Assert.Equal("08/04/2026", Sample.ToDisplayDate());

    [Fact]
    public void DateTime_IsDayFirstWithTwelveHourClockAndSeconds()
        => Assert.Equal("08/04/2026 09:30:05 PM", Sample.ToDisplayDateTime());

    [Fact]
    public void Time_IsTwelveHourWithSeconds() => Assert.Equal("09:30:05 PM", Sample.ToDisplayTime());

    [Fact]
    public void LongDate_IsWrittenOut() => Assert.Equal("August 4, 2026", Sample.ToDisplayDateLong());

    [Fact]
    public void ChartAxisDay_IsMonthFirst() => Assert.Equal("Aug 4", Sample.ToChartDay());

    [Fact]
    public void ChartAxisDay_IsMonthFirstForAPlainDay() =>
        Assert.Equal("Aug 4", DateOnly.FromDateTime(Sample).ToChartDay());

    [Fact]
    public void MorningTimes_KeepTheLeadingZeroAndReadAM()
    {
        var morning = new DateTime(2026, 8, 4, 9, 5, 0);
        Assert.Equal("08/04/2026 09:05:00 AM", morning.ToDisplayDateTime());
    }

    [Fact]
    public void Midnight_ReadsAsTwelveAM()
    {
        // The classic 12-hour bug: "00" instead of "12".
        var midnight = new DateTime(2026, 8, 4, 0, 0, 0);
        Assert.Equal("08/04/2026 12:00:00 AM", midnight.ToDisplayDateTime());
    }

    [Fact]
    public void Noon_ReadsAsTwelvePM()
    {
        var noon = new DateTime(2026, 8, 4, 12, 0, 0);
        Assert.Equal("08/04/2026 12:00:00 PM", noon.ToDisplayDateTime());
    }

    [Fact]
    public void Nullable_Overloads_PassThroughAndReturnNull()
    {
        DateTime? set = Sample;
        Assert.Equal("08/04/2026", set.ToDisplayDate());
        Assert.Equal("August 4, 2026", set.ToDisplayDateLong());

        DateTime? unset = null;
        Assert.Null(unset.ToDisplayDate());
        Assert.Null(unset.ToDisplayDateTime());
        Assert.Null(unset.ToDisplayDateLong());
    }

    [Fact]
    public void PatternConstants_MatchWhatTheHelpersProduce()
    {
        // Date controls and grid columns name these constants rather than repeating a literal;
        // if they ever disagree with the helpers, the same date looks different in a grid and on
        // the card above it.
        Assert.Equal(Sample.ToString(DateTimeViewerExtensions.DatePattern), Sample.ToDisplayDate());
        Assert.Equal(Sample.ToString(DateTimeViewerExtensions.DateTimePattern), Sample.ToDisplayDateTime());
        Assert.Equal(Sample.ToString(DateTimeViewerExtensions.LongDatePattern), Sample.ToDisplayDateLong());
        Assert.Equal(Sample.ToString(DateTimeViewerExtensions.TimePattern), Sample.ToDisplayTime());
    }
}
