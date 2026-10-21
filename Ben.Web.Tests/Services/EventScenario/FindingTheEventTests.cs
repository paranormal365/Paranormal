using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services.EventScenario;

/// <summary>
/// Jim's public event on the home page's map (event-scenario-gaps; Ben's scenario: "His public event gets
/// listed on our site and people can search for it and it will appear in the map on the home page").
/// </summary>
public sealed class FindingTheEventTests
{
    private static PublicEventMenuController Controller(ScenarioWorld w)
        => new(w.Sqlite.Factory, new Mock<Ben.Data.Common.Interfaces.IFileStorageService>().Object);

    private static async Task PlaceAsync(ScenarioWorld w, decimal lat, decimal lon)
    {
        await using var db = await w.Sqlite.NewContextAsync();
        var place = await db.Places.SingleAsync(p => p.Id == w.PlaceId);
        place.Latitude = lat;
        place.Longitude = lon;
        await db.SaveChangesAsync();
    }

    private static async Task<List<PublicHostedEventMapPin>> PinsAsync(ScenarioWorld w)
        => [.. Assert.IsAssignableFrom<IEnumerable<PublicHostedEventMapPin>>(
               Assert.IsType<OkObjectResult>((await Controller(w).MapPins(500, default)).Result).Value)];

    [Fact]
    public async Task An_upcoming_public_event_is_on_the_map_at_an_approximate_point_only()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Published);
        await w.ChangeEventAsync(e => { e.StartsOn = DateTime.UtcNow.Date.AddDays(10); e.EndsOn = e.StartsOn.AddDays(1); });
        await PlaceAsync(w, 35.92551m, -86.86889m);   // Franklin, TN

        var pin = Assert.Single(await PinsAsync(w));
        Assert.Equal(("Overnight at The Factory", "overnight-at-the-factory", "jims-investigations"),
                     (pin.Name, pin.UrlName, pin.OrganizationUrlName));
        // Never the building itself: the same blurred cell a case gets.
        var (lat, lon) = PublicCoordinates.Approximate(35.92551m, -86.86889m);
        Assert.Equal((lat, lon), ((decimal?)pin.Latitude, (decimal?)pin.Longitude));
        Assert.NotEqual(35.92551m, pin.Latitude);
    }

    [Fact]
    public async Task A_draft_an_ended_event_and_one_with_no_point_are_not_on_the_map()
    {
        await using var w = await ScenarioWorld.CreateAsync(HostedEventLifecycleState.Draft);
        await w.ChangeEventAsync(e => { e.StartsOn = DateTime.UtcNow.Date.AddDays(10); e.EndsOn = e.StartsOn; });
        Assert.Empty(await PinsAsync(w));   // a draft, with no point either

        await PlaceAsync(w, 35.9m, -86.8m);
        Assert.Empty(await PinsAsync(w));   // still a draft

        await w.ChangeEventAsync(e => e.LifecycleState = HostedEventLifecycleState.Ended);
        Assert.Empty(await PinsAsync(w));   // over
    }
}
