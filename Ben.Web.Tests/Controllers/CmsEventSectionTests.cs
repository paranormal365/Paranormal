using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// An event on a group's own page, resolved when the page is read (item 235 phase 11).
/// </summary>
/// <remarks>
/// The four event section types existed in the enum, and so in the page editor's menu, but nothing drew
/// them: a group that chose one got "Unknown section type" on its public page. These are what they now
/// show — and, as importantly, what they refuse to.
/// </remarks>
public sealed class CmsEventSectionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid OtherOrgId = Guid.NewGuid();
    private static readonly Guid Host = Guid.NewGuid();

    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    private static async Task<(SqliteTestDb Db, Guid Published, Guid Draft, Guid Theirs)> SeedAsync()
    {
        var sqlite = await SqliteTestDb.CreateAsync();
        await using var db = await sqlite.NewContextAsync();
        var now = DateTime.UtcNow;
        db.Users.Add(new AppUser { Id = Host, Email = "host@example.test", UserName = "host", DateCreated = now });
        db.Organizations.AddRange(
            new Organization { Id = OrgId, Name = "Thomas House", UrlName = "thomas-house", DateCreated = now, CreatedByAppUserId = Host },
            new Organization { Id = OtherOrgId, Name = "Somebody Else", UrlName = "else", DateCreated = now, CreatedByAppUserId = Host });
        var placeId = Guid.NewGuid();
        db.Places.Add(new Place { Id = placeId, Name = "The Thomas House Hotel", City = "Red Boiling Springs", State = "TN", DateCreated = now, CreatedByAppUserId = Host });

        Guid Add(Guid org, string name, HostedEventLifecycleState state)
        {
            var id = Guid.NewGuid();
            db.HostedEvents.Add(new HostedEvent
            {
                Id = id, OrganizationId = org, PlaceId = placeId, Name = name, UrlName = name.ToLowerInvariant().Replace(' ', '-'),
                StartsOn = now.Date.AddDays(10), EndsOn = now.Date.AddDays(11), TimeZoneId = "America/Chicago", LifecycleState = state,
                ProgrammePublishedUtc = now, DateCreated = now, CreatedByAppUserId = Host,
            });
            db.HostedEventNights.Add(new HostedEventNight { Id = Guid.NewGuid(), HostedEventId = id, Date = now.Date.AddDays(10), DateCreated = now, CreatedByAppUserId = Host });
            return id;
        }

        var published = Add(OrgId, "Halloween Lock-In", HostedEventLifecycleState.Published);
        var draft = Add(OrgId, "Next Year", HostedEventLifecycleState.Draft);
        var theirs = Add(OtherOrgId, "Their Weekend", HostedEventLifecycleState.Published);

        db.HostedEventSessions.AddRange(
            new HostedEventSession
            {
                Id = Guid.NewGuid(), HostedEventId = published, Title = "Operating the Ovilus", Capacity = 15, PlacesTaken = 3, RequiresSignUp = true,
                StartsAtUtc = now.Date.AddDays(10).AddHours(26), EndsAtUtc = now.Date.AddDays(10).AddHours(27), LedBy = "Ben",
                DateCreated = now, CreatedByAppUserId = Host,
            },
            new HostedEventSession
            {
                Id = Guid.NewGuid(), HostedEventId = published, Title = "Called-off walk", CalledOffUtc = now,
                StartsAtUtc = now.Date.AddDays(10).AddHours(28), EndsAtUtc = now.Date.AddDays(10).AddHours(29),
                DateCreated = now, CreatedByAppUserId = Host,
            });

        await db.SaveChangesAsync();
        return (sqlite, published, draft, theirs);
    }

    private static async Task<CmsEventSectionRecord> ResolveAsync(SqliteTestDb sqlite, CmsSectionType type, Guid eventId)
    {
        await using var db = await sqlite.NewContextAsync();
        var json = await CmsEmbed.ResolveAsync(db, OrgId, type, JsonSerializer.Serialize(new { eventId }), default);
        return JsonSerializer.Deserialize<CmsEventSectionRecord>(json, Read)!;
    }

    [Fact]
    public void All_four_event_sections_are_resolved_live()
    {
        Assert.True(CmsEmbed.IsEmbed(CmsSectionType.EventProgramme));
        Assert.True(CmsEmbed.IsEmbed(CmsSectionType.EventBooking));
        Assert.True(CmsEmbed.IsEmbed(CmsSectionType.EventGallery));
        Assert.True(CmsEmbed.IsEmbed(CmsSectionType.EventVenue));
    }

    [Fact]
    public async Task A_draft_or_another_groups_event_is_missing_rather_than_leaked()
    {
        var (sqlite, _, draft, theirs) = await SeedAsync();
        await using (sqlite)
        {
            Assert.True((await ResolveAsync(sqlite, CmsSectionType.EventBooking, draft)).Missing);
            Assert.True((await ResolveAsync(sqlite, CmsSectionType.EventBooking, theirs)).Missing);
        }
    }

    [Fact]
    public async Task The_programme_shows_counts_and_a_cancelled_session_as_cancelled()
    {
        var (sqlite, published, _, _) = await SeedAsync();
        await using (sqlite)
        {
            var section = await ResolveAsync(sqlite, CmsSectionType.EventProgramme, published);
            Assert.False(section.Missing);
            Assert.Equal("/o/thomas-house/events/halloween-lock-in", section.Url);

            var ovilus = Assert.Single(section.Programme!, s => s.Title == "Operating the Ovilus");
            Assert.Equal("3 of 15", ovilus.Places);
            Assert.True(Assert.Single(section.Programme!, s => s.Title == "Called-off walk").IsCancelled);
        }
    }

    [Fact]
    public async Task The_venue_section_names_the_place_and_keeps_a_story_it_was_not_lent()
    {
        var (sqlite, published, _, _) = await SeedAsync();
        await using (sqlite)
        {
            await using (var db = await sqlite.NewContextAsync())
            {
                var place = db.Places.Single();
                db.OrganizationVenueProfiles.Add(new OrganizationVenueProfile
                {
                    Id = Guid.NewGuid(), OrganizationId = OtherOrgId, PlaceId = place.Id, VerifiedUtc = DateTime.UtcNow,
                    History = "Built in 1890.", DateCreated = DateTime.UtcNow, CreatedByAppUserId = Host,
                });
                await db.SaveChangesAsync();
            }

            var section = await ResolveAsync(sqlite, CmsSectionType.EventVenue, published);
            Assert.Equal("The Thomas House Hotel", section.Venue!.PlaceName);
            Assert.Equal("Somebody Else", section.Venue.RunBy);
            Assert.Null(section.Venue.History);   // another group's story, not lent to this event
        }
    }
}
