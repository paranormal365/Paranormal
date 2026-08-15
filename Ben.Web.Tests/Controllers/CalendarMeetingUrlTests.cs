using Ben.Data.WebApi.Controllers.Entities;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// Covers how a pasted meeting link is tidied before storage.
/// </summary>
/// <remarks>
/// A link that goes nowhere is worse than no link — someone will click it while a meeting is
/// starting — so anything unusable is stored as null rather than as text that renders a dead anchor.
/// </remarks>
public class CalendarMeetingUrlTests
{
    [Theory]
    [InlineData("https://zoom.us/j/123", "https://zoom.us/j/123")]
    [InlineData("http://teams.microsoft.com/l/x", "http://teams.microsoft.com/l/x")]
    // People paste the bare host far more often than the full URL.
    [InlineData("zoom.us/j/123", "https://zoom.us/j/123")]
    [InlineData("  meet.google.com/abc-defg  ", "https://meet.google.com/abc-defg")]
    public void A_usable_link_is_kept_and_given_a_scheme_if_missing(string input, string expected)
        => Assert.Equal(expected, OrgCalendarEventController.NormaliseUrl(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_stays_nothing(string? input)
        => Assert.Null(OrgCalendarEventController.NormaliseUrl(input));

    [Theory]
    // Non-web schemes are refused rather than stored: javascript: in particular would become a
    // clickable script link on a page that renders the event.
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/x")]
    public void A_non_web_scheme_is_refused(string input)
        => Assert.Null(OrgCalendarEventController.NormaliseUrl(input));
}
