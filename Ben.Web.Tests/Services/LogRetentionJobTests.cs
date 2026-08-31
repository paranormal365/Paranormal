using Ben.Data.WebApi.Services.Scheduling;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The two guards standing between a configuration file and a DELETE statement.
/// </summary>
/// <remarks>
/// This is the only scheduled job that removes rows, so its refusals matter more than its
/// successes. Each test names the accident it prevents rather than the branch it covers.
/// </remarks>
public class LogRetentionJobTests
{
    [Fact]
    public void No_setting_at_all_means_the_default_window()
    {
        Assert.Equal(LogRetentionJob.DefaultDays, LogRetentionJob.WindowDays(null));
    }

    [Fact]
    public void Zero_switches_the_job_off_rather_than_deleting_everything()
    {
        // The reading that matters: 0 is "keep nothing" read literally, which would empty the
        // table. It means "off".
        Assert.Null(LogRetentionJob.WindowDays(0));
    }

    [Fact]
    public void A_negative_window_is_off_too_and_never_a_future_cutoff()
    {
        // -30 taken literally is a cutoff thirty days in the FUTURE, which deletes every row in
        // the table including ones written a second ago.
        Assert.Null(LogRetentionJob.WindowDays(-30));
    }

    [Fact]
    public void A_window_below_the_floor_is_clamped_up_not_obeyed()
    {
        // A mistyped 1 is the realistic accident. Obeying it destroys almost the whole table;
        // refusing outright leaves the site with no retention at all. Clamping does neither.
        Assert.Equal(LogRetentionJob.MinimumDays, LogRetentionJob.WindowDays(1));
        Assert.Equal(LogRetentionJob.MinimumDays, LogRetentionJob.WindowDays(LogRetentionJob.MinimumDays - 1));
    }

    [Fact]
    public void A_deliberate_window_is_honoured_exactly()
    {
        Assert.Equal(90, LogRetentionJob.WindowDays(90));
        Assert.Equal(LogRetentionJob.MinimumDays, LogRetentionJob.WindowDays(LogRetentionJob.MinimumDays));
    }

    [Theory]
    [InlineData("Logs")]
    [InlineData("ApplicationLogs")]
    [InlineData("_staging_logs2")]
    public void An_ordinary_table_name_is_allowed(string name)
    {
        Assert.True(LogRetentionJob.IsPlainIdentifier(name));
    }

    [Theory]
    [InlineData("Logs; DROP TABLE AuditLogs--")]   // the reason the guard exists
    [InlineData("Logs]")]                          // escapes the bracket quoting
    [InlineData("dbo.Logs")]                       // schema-qualified: not what the command builds
    [InlineData("Log s")]
    [InlineData("2Logs")]                          // not a legal identifier start
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_is_not_a_plain_identifier_is_refused(string? name)
    {
        Assert.False(LogRetentionJob.IsPlainIdentifier(name));
    }

    [Fact]
    public void The_batch_ceiling_is_a_multiple_of_the_batch_size()
    {
        // Not pedantry: the loop stops when a batch comes back short, so a ceiling that is not a
        // whole number of batches would stop one batch late and exceed its own limit.
        Assert.True(LogRetentionJob.MaximumPerPass >= LogRetentionJob.BatchSize);
        Assert.Equal(0, LogRetentionJob.MaximumPerPass % LogRetentionJob.BatchSize);
    }
}
