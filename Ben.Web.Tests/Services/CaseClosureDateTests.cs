using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The closed date follows the status both ways.
/// </summary>
/// <remarks>
/// Written against the fault it fixes: the rule only ever SET the date, so Ben's case #2026-002
/// read "Closed 09/19/2026" under a green Active badge (2026-09-19). Every test below that
/// expects null was run against the old one-way rule first and failed there.
/// </remarks>
public sealed class CaseClosureDateTests
{
    private static readonly DateTime Now   = new(2026, 9, 19, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Older = new(2026, 9, 10, 9, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Public)]
    [InlineData(CaseStatus.Haunted)]
    public void Closing_a_case_dates_it(CaseStatus status)
        => Assert.Equal(Now, CaseClosureDate.After(status, null, Now));

    [Theory]
    [InlineData(CaseStatus.Closed)]
    [InlineData(CaseStatus.Public)]
    [InlineData(CaseStatus.Haunted)]
    public void A_case_that_is_already_dated_keeps_the_first_date(CaseStatus status)
        => Assert.Equal(Older, CaseClosureDate.After(status, Older, Now));

    [Theory]
    [InlineData(CaseStatus.Proposed)]
    [InlineData(CaseStatus.Accepted)]
    [InlineData(CaseStatus.Active)]
    [InlineData(CaseStatus.Summarized)]
    public void Working_it_again_clears_the_date(CaseStatus status)
        => Assert.Null(CaseClosureDate.After(status, Older, Now));

    [Theory]
    [InlineData(CaseStatus.Proposed)]
    [InlineData(CaseStatus.Active)]
    public void A_case_that_was_never_closed_stays_undated(CaseStatus status)
        => Assert.Null(CaseClosureDate.After(status, null, Now));

    /// <summary>
    /// Handing a case to another group is not reopening it, so its date is left alone.
    /// </summary>
    [Fact]
    public void Transferring_touches_neither()
    {
        Assert.Equal(Older, CaseClosureDate.After(CaseStatus.Transferred, Older, Now));
        Assert.Null(CaseClosureDate.After(CaseStatus.Transferred, null, Now));
    }

    /// <summary>
    /// A lapsed subscription restores the case's old status when the group renews
    /// (<c>Case.StatusBeforePause</c>), so pausing must not throw away the closed date of a case
    /// that was published before the lapse — it would come back wrong.
    /// </summary>
    [Fact]
    public void Pausing_for_a_lapsed_subscription_keeps_the_date()
    {
        Assert.Equal(Older, CaseClosureDate.After(CaseStatus.Paused, Older, Now));
        Assert.Null(CaseClosureDate.After(CaseStatus.Paused, null, Now));
    }

    /// <summary>
    /// Every status is either closing, working, or deliberately neither — and no status is both.
    /// </summary>
    /// <remarks>
    /// The working set is <c>&lt;= Summarized</c>, which the codebase already uses to mean "open".
    /// A status appended below Summarized would silently join it, so this says out loud which
    /// statuses are in which group and fails when a new one lands without a decision.
    /// </remarks>
    [Fact]
    public void Every_status_is_placed_on_purpose()
    {
        foreach (var status in Enum.GetValues<CaseStatus>())
            Assert.False(CaseClosureDate.IsClosing(status) && CaseClosureDate.IsWorking(status),
                $"{status} is counted as both closing and working.");

        var neither = Enum.GetValues<CaseStatus>()
            .Where(s => !CaseClosureDate.IsClosing(s) && !CaseClosureDate.IsWorking(s))
            .ToArray();

        Assert.Equal([CaseStatus.Transferred, CaseStatus.Paused], neither);
    }
}
