using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// An ad that leads to an event, shown only while the event is worth going to (item 235 phase 11).
/// </summary>
public sealed class EventAdTests
{
    private sealed class Factory(DbContextOptions<BenDataContext> opts) : IDbContextFactory<BenDataContext>
    {
        public BenDataContext CreateDbContext() => new(opts);
        public Task<BenDataContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(new BenDataContext(opts));
    }

    private static async Task<(IDbContextFactory<BenDataContext> Factory, Guid AdId, Guid EventId)> SeedAsync(
        HostedEventLifecycleState state, int endsInDays)
    {
        var factory = new Factory(new DbContextOptionsBuilder<BenDataContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var db = await ((IDbContextFactory<BenDataContext>)factory).CreateDbContextAsync();
        var orgId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var adId = Guid.NewGuid();
        var user = Guid.NewGuid();

        db.Organizations.Add(new Organization { Id = orgId, Name = "Thomas House", UrlName = "thomas-house", DateCreated = DateTime.UtcNow });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = eventId, OrganizationId = orgId, PlaceId = Guid.NewGuid(), Name = "Halloween Lock-In", UrlName = "lock-in",
            StartsOn = DateTime.UtcNow.Date.AddDays(endsInDays - 1), EndsOn = DateTime.UtcNow.Date.AddDays(endsInDays),
            LifecycleState = state, DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
        });
        db.OrganizationAds.Add(new OrganizationAd
        {
            Id = adId, OrganizationId = orgId, Headline = "A night at the Thomas House", Body = "Two nights, a séance.",
            TargetKind = "event", HostedEventId = eventId, Status = OrganizationAdStatus.Approved,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = user,
        });
        await db.SaveChangesAsync();
        return (factory, adId, eventId);
    }

    private static PublicPromotedGroupsController Promoted(IDbContextFactory<BenDataContext> factory)
        => new(factory, new Mock<IFileStorageService>().Object);

    private static async Task<List<PromotedGroupCard>> CardsAsync(IDbContextFactory<BenDataContext> factory)
        => ((IEnumerable<PromotedGroupCard>)Assert.IsType<OkObjectResult>((await Promoted(factory).Get(10, null, null, default)).Result).Value!).ToList();

    [Fact]
    public async Task An_event_ad_is_shown_with_its_event_and_leads_to_the_event_page()
    {
        var (factory, adId, _) = await SeedAsync(HostedEventLifecycleState.Published, endsInDays: 20);

        var card = Assert.Single(await CardsAsync(factory));
        Assert.Equal("Halloween Lock-In", card.EventName);

        var target = Assert.IsType<PromotedClickTarget>(Assert.IsType<OkObjectResult>((await Promoted(factory).Click(adId, default)).Result).Value);
        Assert.Equal("event", target.TargetKind);
        Assert.Equal("lock-in", target.EventUrlName);
    }

    [Fact]
    public async Task An_event_ad_stops_showing_once_the_event_is_over()
    {
        var (factory, _, _) = await SeedAsync(HostedEventLifecycleState.Published, endsInDays: -1);
        Assert.Empty(await CardsAsync(factory));
    }

    [Fact]
    public async Task An_event_ad_is_not_shown_for_an_event_that_was_called_off()
    {
        var (factory, _, _) = await SeedAsync(HostedEventLifecycleState.Cancelled, endsInDays: 20);
        Assert.Empty(await CardsAsync(factory));
    }
}
