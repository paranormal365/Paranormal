using Ben.Service.Models.FieldLaunches;
using Ben.Web.Website.Library.Manage.Events;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// What the organizer's screens decide for themselves (event-scenario-gaps): the places a party is put in,
/// and the sentence after starting the sessions.
/// </summary>
public sealed class EventScenarioScreensTests
{
    /// <summary>
    /// A party of three in seats holds three seats on one night. The decision sheet kept its choices by night,
    /// so confirming kept one seat and the server dropped the other two.
    /// </summary>
    [Fact]
    public void Confirming_a_party_keeps_every_place_it_holds_on_a_night()
    {
        var night = Guid.NewGuid();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        var sent = BookingDecisionSheet.Choices([(night, a), (night, b), (night, c)]);

        Assert.Equal([a, b, c], sent.Select(s => s.HostedEventLayoutUnitId!.Value));
        Assert.All(sent, s => Assert.Equal(night, s.HostedEventNightId));
        // The same seat chosen twice is one place.
        Assert.Single(BookingDecisionSheet.Choices([(night, a), (night, a)]));
    }

    [Fact]
    public void Starting_the_sessions_says_who_it_reached_and_how()
    {
        var launch = new FieldLaunchRecord(Guid.NewGuid(), "hosted-event", null, null, Guid.NewGuid(), "Overnight", null, "Jim's", "Jim",
            DateTime.UtcNow, DateTime.UtcNow.AddHours(6), DateTime.UtcNow.AddHours(12), false, "ishaunted://field-kit/launch/x");
        Assert.Equal("Sent to 12 people. 9 of them have the app and were notified.",
            StartSessionsCard.Said(new LaunchOutcomeRecord(launch, 12, 9, 9, true)));
        Assert.Equal("Sent to 1 person. Notifications aren't set up on this site, so it's a card in their feed only.",
            StartSessionsCard.Said(new LaunchOutcomeRecord(launch, 1, 0, 0, false)));
    }
}
