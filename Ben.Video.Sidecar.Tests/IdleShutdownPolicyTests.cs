using Ben.Video.Sidecar.Lifetime;
using Xunit;

namespace Ben.Video.Sidecar.Tests;

public sealed class IdleShutdownPolicyTests
{
    private static readonly TimeSpan Fifteen = TimeSpan.FromMinutes(15);

    [Fact]
    public void It_stops_once_nothing_has_asked_it_for_anything()
    {
        Assert.True(IdleShutdownPolicy.ShouldStop(TimeSpan.FromMinutes(15), Fifteen, activeJobs: 0));
        Assert.True(IdleShutdownPolicy.ShouldStop(TimeSpan.FromHours(3), Fifteen, activeJobs: 0));
    }

    [Fact]
    public void It_stays_up_while_it_is_still_being_used()
    {
        Assert.False(IdleShutdownPolicy.ShouldStop(TimeSpan.Zero, Fifteen, activeJobs: 0));
        Assert.False(IdleShutdownPolicy.ShouldStop(TimeSpan.FromMinutes(14.9), Fifteen, activeJobs: 0));
    }

    [Fact]
    public void A_render_in_flight_is_never_interrupted_however_quiet_it_has_been()
    {
        // A long export makes no requests while it runs: the job was submitted once and the client
        // polls for it. Without this, the sidecar would stop in the middle of the render somebody
        // is waiting for.
        Assert.False(IdleShutdownPolicy.ShouldStop(TimeSpan.FromHours(2), Fifteen, activeJobs: 1));
    }

    [Fact]
    public void A_timeout_of_zero_or_less_means_never_stop()
    {
        Assert.False(IdleShutdownPolicy.ShouldStop(TimeSpan.FromDays(1), TimeSpan.Zero, activeJobs: 0));
        Assert.False(IdleShutdownPolicy.ShouldStop(TimeSpan.FromDays(1), TimeSpan.FromMinutes(-1), activeJobs: 0));
    }

    [Fact]
    public void The_default_is_long_enough_not_to_cost_a_re_render_mid_session()
    {
        Assert.True(IdleShutdownPolicy.DefaultIdleTimeout >= TimeSpan.FromMinutes(10),
            "a short timeout drops the editor's segment index during an ordinary pause");
    }

    // 1.1.0 armed the idle timeout everywhere. On Windows nothing starts the sidecar again after it
    // stops - the installer runs it once at login - so it quit fifteen minutes after login and was
    // gone for the rest of the day. These pin the rule that replaced it.

    [Fact]
    public void Where_something_starts_it_again_the_configured_timeout_is_armed()
        => Assert.Equal(Fifteen, IdleShutdownPolicy.EffectiveTimeout(Fifteen, restartedOnDemand: true));

    [Fact]
    public void Where_nothing_would_start_it_again_it_never_arms_a_timeout()
        => Assert.Equal(TimeSpan.Zero, IdleShutdownPolicy.EffectiveTimeout(Fifteen, restartedOnDemand: false));

    [Fact]
    public void A_sidecar_nothing_can_restart_stays_up_however_long_it_has_been_quiet()
    {
        // The two halves together, as the running process sees them: a Windows install left quiet
        // all day must still be there when the editor finally opens.
        var armed = IdleShutdownPolicy.EffectiveTimeout(IdleShutdownPolicy.DefaultIdleTimeout, restartedOnDemand: false);
        Assert.False(IdleShutdownPolicy.ShouldStop(TimeSpan.FromHours(10), armed, activeJobs: 0));
    }

    [Fact]
    public void Switching_it_off_in_configuration_still_wins_where_it_could_be_restarted()
        => Assert.Equal(TimeSpan.Zero, IdleShutdownPolicy.EffectiveTimeout(TimeSpan.Zero, restartedOnDemand: true));
}
