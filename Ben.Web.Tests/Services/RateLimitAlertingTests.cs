using Ben.Data.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Telling a SuperAdmin when a rate limit is turning people away, without telling them so often
/// they stop reading (item 199).
/// </summary>
/// <remarks>
/// <para>Ben asked for monitoring and then set the shape of it: <i>"Doesn't matter if it is
/// 50,000 times… one time letting me know is enough. Then just a place to track more than the one
/// time. So, 650 takes less of a look than 6,500."</i> The half that needs testing is not that a
/// message is sent — it is that the thing does not flood, because a limit under real pressure
/// refuses continuously and a message per refusal would bury every other notice on the site.</para>
///
/// <para>Only <see cref="RateLimitAlerting.Record"/> is exercised here: it is deliberately
/// separate from delivery so the decision needs no database, which is also why it is safe to call
/// on the rejection path of every refused request. Delivery is gated a second time by the row's
/// <c>DateNotified</c>, so a restart cannot produce a second message.</para>
/// </remarks>
public sealed class RateLimitAlertingTests
{
    private DateTime _now = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);

    private RateLimitAlerting Subject() => new(
        dbFactory: null!,                 // never reached: Record does no database work
        messages:  null!,
        logger:    NullLogger<RateLimitAlerting>.Instance,
        now:       () => _now);

    private const string Policy = RateLimiting.EventAttendancePolicy;

    /// <summary>A handful of refusals is normal and says nothing.</summary>
    [Fact]
    public void A_few_refusals_do_not_raise_anything()
    {
        var alerting = Subject();

        for (var i = 0; i < RateLimitAlerting.AlertThreshold - 1; i++)
            Assert.Null(alerting.Record(Policy, $"10.0.0.{i}"));
    }

    /// <summary>Crossing the threshold raises exactly one message, not one per refusal.</summary>
    [Fact]
    public void Crossing_the_threshold_raises_one_message()
    {
        var alerting = Subject();
        var raised = 0;

        for (var i = 0; i < RateLimitAlerting.AlertThreshold * 4; i++)
            if (alerting.Record(Policy, "203.0.113.7") is not null) raised++;

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Fifty thousand more refusals raise nothing further — Ben's rule, tested at his number.
    /// </summary>
    /// <remarks>
    /// No clock is advanced, because there is no interval after which it starts talking again.
    /// Being told twice is a deliberate act on the admin page, never the passage of time.
    /// </remarks>
    [Fact]
    public void It_never_speaks_a_second_time_however_many_more_there_are()
    {
        var alerting = Subject();
        var raised = 0;

        for (var i = 0; i < 50_000; i++)
            if (alerting.Record(Policy, $"10.0.{i / 256 % 256}.{i % 256}") is not null) raised++;

        _now = _now.AddDays(3);

        for (var i = 0; i < 50_000; i++)
            if (alerting.Record(Policy, "a") is not null) raised++;

        Assert.Equal(1, raised);
    }

    /// <summary>Each limit is counted on its own — a noisy one must not silence a quiet one.</summary>
    [Fact]
    public void Policies_are_counted_separately()
    {
        var alerting = Subject();

        for (var i = 0; i < RateLimitAlerting.AlertThreshold * 3; i++)
            alerting.Record(RateLimiting.AuthPolicy, "attacker");

        RateLimitAlert? tours = null;
        for (var i = 0; i < RateLimitAlerting.AlertThreshold && tours is null; i++)
            tours = alerting.Record(Policy, $"guest-{i}");

        Assert.NotNull(tours);
        Assert.Equal(Policy, tours!.PolicyName);
    }

    /// <summary>
    /// The message distinguishes a crowd from a script, which is the only thing that tells the
    /// reader whether to raise the limit or leave it alone.
    /// </summary>
    [Fact]
    public void The_message_says_whether_it_is_a_crowd_or_one_caller()
    {
        var crowd = new RateLimitAlert(Policy, Refusals: 40, DistinctCallers: 31);
        var script = new RateLimitAlert(Policy, Refusals: 40, DistinctCallers: 1);

        Assert.Contains("31 different addresses", crowd.Body());
        Assert.Contains("too low", crowd.Body());
        Assert.Contains("Site settings", crowd.Body());

        Assert.Contains("a single address", script.Body());
        Assert.DoesNotContain("Site settings", script.Body());

        // The reader must know not to wait for a second message, or they will wait instead of look.
        Assert.Contains("only message", crowd.Body());
        Assert.Contains("Rate Limits", crowd.Body());
    }

    /// <summary>The subject names the limit in words a person can act on, not the policy key.</summary>
    [Fact]
    public void The_subject_names_the_limit_in_plain_words()
    {
        Assert.Equal(
            "Rate limit reached — public event sign-up",
            new RateLimitAlert(Policy, 40, 31).Subject());

        Assert.Equal(
            "Rate limit reached — general requests",
            new RateLimitAlert(RateLimiting.GlobalPolicyName, 40, 1).Subject());
    }
}
