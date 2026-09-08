using Ben.Data.Common.Enums;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// When a published report has moved on from what the client was sent (W-A14).
/// </summary>
/// <remarks>
/// The 2026-09-06 evaluation found a published report fully editable with nothing saying so and
/// no way to re-issue it. The client is reading a PDF generated live, so a later edit reaches
/// them silently and the notice on their case board still describes the version they were first
/// handed. This rule is what puts the Re-publish button on screen.
/// </remarks>
public class ReportPublishStateTests
{
    private static readonly DateTime Published = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static CaseReportDetail Report(CaseReportStatus status, DateTime? publishedAt, DateTime? updated) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Final Report", null, null, status,
            null, publishedAt, Published.AddDays(-7), [], false, updated);

    [Fact]
    public void A_published_report_edited_afterwards_has_unpublished_changes()
    {
        Assert.True(Report(CaseReportStatus.Published, Published, Published.AddMinutes(5))
            .HasUnpublishedChanges);
    }

    [Fact]
    public void A_report_untouched_since_publishing_does_not()
    {
        Assert.False(Report(CaseReportStatus.Published, Published, Published)
            .HasUnpublishedChanges);
    }

    /// <summary>
    /// Publish writes PublishedAt and DateUpdated in one statement.
    /// </summary>
    /// <remarks>
    /// Without a moment's slack, a clock that resolved the two a tick apart would put every
    /// freshly published report into the "edited since published" state — the button would appear
    /// on the same click that made it unnecessary.
    /// </remarks>
    [Fact]
    public void A_tick_between_the_two_timestamps_is_not_an_edit()
    {
        Assert.False(Report(CaseReportStatus.Published, Published, Published.AddMilliseconds(4))
            .HasUnpublishedChanges);
    }

    /// <summary>
    /// A draft is not "edited since published" — it was never published.
    /// </summary>
    [Fact]
    public void A_draft_never_has_unpublished_changes()
    {
        Assert.False(Report(CaseReportStatus.Draft, null, Published.AddDays(1))
            .HasUnpublishedChanges);
    }

    /// <summary>
    /// A report from before this field existed carries no DateUpdated, and says nothing rather
    /// than guessing.
    /// </summary>
    [Fact]
    public void No_update_stamp_makes_no_claim()
    {
        Assert.False(Report(CaseReportStatus.Published, Published, null)
            .HasUnpublishedChanges);
    }
}
