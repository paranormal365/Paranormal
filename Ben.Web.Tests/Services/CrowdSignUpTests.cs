using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A tour crowd signing up at the meeting point must not be refused for looking like one script
/// (item 199).
/// </summary>
/// <remarks>
/// <para><b>The situation these protect.</b> Ben, describing how a ghost walking tour actually
/// earns: "They may have 30 people 3 times each night and 30 people 3 times next night who are
/// new. These are just their guests, but not members of their group." Every guest needs an
/// account — that is the requirement, not the obstacle, because the account is how a guest
/// becomes an app user and how their evidence reaches the group. So ninety strangers a night
/// sign themselves up from the venue's wifi, and to a per-IP rate limiter they are one caller.</para>
///
/// <para><b>Why a bigger server would not have fixed it.</b> The global ceiling is a per-caller
/// refusal, not a capacity ceiling: it rejects one address exceeding it regardless of how much
/// the machine could have served. A crowd behind a single NAT would be turned away on any
/// hardware.</para>
/// </remarks>
public sealed class CrowdSignUpTests
{
    /// <summary>
    /// The guest sign-up endpoints declare the crowd policy, which is what takes them out from
    /// under the global ceiling.
    /// </summary>
    [Fact]
    public void Guest_sign_up_declares_the_crowd_policy()
    {
        var attribute = typeof(PublicEventAttendanceController)
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(RateLimiting.EventAttendancePolicy, attribute!.PolicyName);
    }

    /// <summary>
    /// An endpoint carrying its own policy is exempt from the global ceiling; everything else
    /// still answers to it.
    /// </summary>
    /// <remarks>
    /// This is the test that matters, because without the exemption the whole change is
    /// decorative: the global limiter runs on every request, the stricter of the two wins, and a
    /// policy set above 600 would read back correctly from the settings page while changing
    /// nothing. Reflection is used because the decision is a private detail of the limiter setup
    /// and asserting on it directly beats asserting on a 429 that could come from either limiter.
    /// </remarks>
    [Theory]
    [InlineData(true,  true)]
    [InlineData(false, false)]
    public void Only_an_endpoint_with_its_own_policy_escapes_the_global_ceiling(
        bool declaresPolicy, bool expectedExempt)
    {
        var method = typeof(RateLimiting).GetMethod(
            "HasOwnPolicy",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var metadata = declaresPolicy
            ? new EndpointMetadataCollection(new EnableRateLimitingAttribute("anything"))
            : EndpointMetadataCollection.Empty;

        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));

        Assert.Equal(expectedExempt, (bool)method!.Invoke(null, [context])!);
    }

    /// <summary>
    /// The crowd allowance is well clear of a sold-out night: thirty guests signing up and
    /// reloading, three sessions overlapping, all from one address.
    /// </summary>
    [Fact]
    public void The_crowd_allowance_covers_a_full_night_from_one_address()
    {
        const int guestsPerSession = 30;
        const int sessionsOverlapping = 3;
        const int callsPerGuest = 3;   // ask, open the link, confirm

        Assert.True(
            RateLimiting.DefaultEventAttendancePerMinute
                >= guestsPerSession * sessionsOverlapping * callsPerGuest,
            "A sold-out night arriving at once must fit inside the per-minute allowance.");
    }

    /// <summary>
    /// The mailer ceiling leaves real events far more room than they need, while still being a
    /// bound — three links per seat, and a floor for events that never stated a capacity.
    /// </summary>
    [Theory]
    [InlineData(30,  90)]    // a walking tour
    [InlineData(200, 600)]   // a large public hunt
    public void An_event_may_issue_several_invitations_per_seat(int capacity, int expected)
    {
        var ceiling = Math.Max(
            capacity * PublicEventAttendanceController.InviteCeilingMultiple,
            PublicEventAttendanceController.InviteCeilingFloor);

        Assert.Equal(Math.Max(expected, PublicEventAttendanceController.InviteCeilingFloor), ceiling);
        Assert.True(ceiling > capacity,
            "An event must be able to invite more people than it seats — not everyone turns up.");
    }

    /// <summary>An event with no stated capacity still gets a bound rather than none.</summary>
    [Fact]
    public void An_event_without_a_capacity_is_still_bounded()
    {
        Assert.True(PublicEventAttendanceController.InviteCeilingFloor > 0);
        Assert.True(PublicEventAttendanceController.InviteCeilingFloor
                    > 30 * PublicEventAttendanceController.InviteCeilingMultiple,
            "The floor must be above a normal tour, or it would refuse ordinary evenings.");
    }
}
