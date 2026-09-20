using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

/// <summary>
/// The rules behind the on/off switch in the Native acceleration panel.
/// </summary>
/// <remarks>
/// Ben asked for this so a Windows user can turn the sidecar off without going to Task Manager,
/// and back on without an installer. The two halves are not symmetrical: off is a request to a
/// running program, on is a protocol link the operating system acts on.
/// </remarks>
public sealed class SidecarSwitchTests
{
    [Theory]
    [InlineData("Windows")]
    [InlineData("Win32")]     // what older browsers report
    [InlineData("windows")]
    public void Windows_gets_the_switch(string platform)
    {
        Assert.True(SidecarSwitch.IsOfferedOn(platform));
    }

    /// <summary>
    /// macOS starts the sidecar on demand through launchd and stops it when idle, so a switch
    /// there would offer to do something that already happens by itself.
    /// </summary>
    [Theory]
    [InlineData("macOS")]
    [InlineData("Linux")]
    [InlineData("Android")]
    [InlineData("")]
    public void Everywhere_else_does_not(string platform)
    {
        Assert.False(SidecarSwitch.IsOfferedOn(platform));
    }

    /// <summary>
    /// A browser that will not say gets no switch. A switch that does nothing when pressed is
    /// worse than no switch, and nothing else about the panel depends on this.
    /// </summary>
    [Fact]
    public void A_browser_that_will_not_say_gets_no_switch()
    {
        Assert.False(SidecarSwitch.IsOfferedOn(null));
    }

    [Fact]
    public void The_sidecar_accepting_means_it_stopped()
    {
        Assert.Equal(SidecarStopOutcome.Stopped, SidecarSwitch.ReadStopResponse(202));
        Assert.Equal(SidecarStopOutcome.Stopped, SidecarSwitch.ReadStopResponse(200));
    }

    /// <summary>
    /// 409 is the sidecar refusing to throw away a running export. The person cannot see what it
    /// is doing, so this has to reach them as a question rather than as a failure.
    /// </summary>
    [Fact]
    public void A_refusal_over_work_in_progress_is_not_a_failure()
    {
        Assert.Equal(SidecarStopOutcome.WorkInProgress, SidecarSwitch.ReadStopResponse(409));
    }

    /// <summary>
    /// Including 401: a token that no longer works is not a reason to tell somebody the sidecar
    /// stopped, because it did not.
    /// </summary>
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public void Anything_else_is_a_failure(int status)
    {
        Assert.Equal(SidecarStopOutcome.Failed, SidecarSwitch.ReadStopResponse(status));
    }

    [Fact]
    public void What_is_still_running_is_counted_properly_in_the_wording()
    {
        Assert.Contains("Something is still rendering",
            SidecarSwitch.Explain(SidecarStopOutcome.WorkInProgress, activeJobs: 1));
        Assert.Contains("3 jobs are still running",
            SidecarSwitch.Explain(SidecarStopOutcome.WorkInProgress, activeJobs: 3));
    }

    [Fact]
    public void Every_outcome_says_something_and_none_of_it_is_jargon()
    {
        foreach (var outcome in Enum.GetValues<SidecarStopOutcome>())
        {
            var text = SidecarSwitch.Explain(outcome, activeJobs: 1);

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("HTTP", text);
            Assert.DoesNotContain("409", text);
            Assert.DoesNotContain("sidecar,", text);   // no stray list formatting
        }
    }

    /// <summary>
    /// The link is what the installer registers under HKCU and what the package manifest declares;
    /// if this string and those two ever disagree, the switch silently does nothing.
    /// </summary>
    [Fact]
    public void The_launch_link_uses_the_registered_scheme()
    {
        Assert.StartsWith("benvideo-sidecar:", SidecarSwitch.LaunchUri);
    }
}
