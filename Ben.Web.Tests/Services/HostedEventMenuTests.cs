using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The order a weekend's food is read in (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-12: <i>"they may have a breakfast and lunch and dinner and snacks I guess.
/// Maybe we just make it part of the Menu overall."</i> So a night carries as many sittings as the
/// venue serves, and the whole event is one card.</para>
///
/// <para><b>The rule these pin is that a night is a stay-period, not a calendar day.</b> It runs
/// from the evening people arrive through the morning they come down, which is the only way
/// breakfast has a night to belong to — and it is why sorting a night's sittings by the clock
/// would print the weekend backwards.</para>
/// </remarks>
public sealed class HostedEventMenuTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PlaceId = Guid.NewGuid();
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid HostId = Guid.NewGuid();
    private static readonly DateTime Friday = new(2026, 10, 30);
    private static readonly DateTime Saturday = new(2026, 10, 31);

    private sealed record Seeded(Guid FridayId, Guid SaturdayId);

    [Fact]
    public async Task A_night_holds_dinner_supper_and_the_next_mornings_breakfast()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            AddSitting(db, seeded.FridayId, "Dinner", new TimeSpan(19, 0, 0), order: 0);
            AddSitting(db, seeded.FridayId, "Late supper", new TimeSpan(23, 30, 0), order: 1);
            AddSitting(db, seeded.FridayId, "Breakfast", new TimeSpan(8, 0, 0), order: 2);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var menus = await HostedEventMenuController.MenusAsync(db, EventId, default);

            // Read in the host's order, NOT by the clock: breakfast at eight belongs after the
            // dinner it followed, because the night ran through it.
            Assert.Equal(["Dinner", "Late supper", "Breakfast"],
                menus.Menus.Select(m => m.Title).ToArray());
        }
    }

    [Fact]
    public async Task Nights_are_read_in_order_whatever_order_the_sittings_were_written_in()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            AddSitting(db, seeded.SaturdayId, "Saturday dinner", new TimeSpan(19, 0, 0), order: 0);
            AddSitting(db, seeded.FridayId, "Friday dinner", new TimeSpan(19, 0, 0), order: 0);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var menus = await HostedEventMenuController.MenusAsync(db, EventId, default);

            Assert.Equal(["Friday dinner", "Saturday dinner"],
                menus.Menus.Select(m => m.Title).ToArray());
            Assert.Equal(Friday, menus.Menus[0].NightDate);
        }
    }

    [Fact]
    public async Task A_sitting_carries_its_dishes_in_the_order_the_kitchen_serves_them()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var dinner = AddSitting(db, seeded.FridayId, "Dinner", new TimeSpan(19, 0, 0), order: 0);
            // Written pudding-first, to prove the order comes from SortOrder and not from
            // whatever the database hands back.
            AddDish(db, dinner, "Treacle tart", "Pudding", order: 2, tags: "contains nuts");
            AddDish(db, dinner, "Soup", "Starter", order: 0, tags: "vegan");
            AddDish(db, dinner, "Beef", "Main", order: 1, tags: null);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var menus = await HostedEventMenuController.MenusAsync(db, EventId, default);
            var dinner = Assert.Single(menus.Menus);

            Assert.Equal(["Soup", "Beef", "Treacle tart"],
                dinner.Items.Select(i => i.Name).ToArray());

            // A dish's tags describe the FOOD and go to everybody who can see the menu. A guest's
            // own allergy is a different row with a different audience and is never in here.
            Assert.Equal("contains nuts", dinner.Items[2].DietaryTags);
        }
    }

    [Fact]
    public async Task An_event_with_no_menus_answers_with_an_empty_card_rather_than_nothing()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await SeedAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        var menus = await HostedEventMenuController.MenusAsync(db, EventId, default);

        Assert.Equal(EventId, menus.HostedEventId);
        Assert.Empty(menus.Menus);
    }

    [Fact]
    public async Task Deleting_a_night_takes_its_sittings_with_it()
    {
        // A menu that outlived its night would go on serving Sunday dinner to nobody.
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var seeded = await SeedAsync(sqlite);

        await using (var db = await sqlite.NewContextAsync())
        {
            var dinner = AddSitting(db, seeded.SaturdayId, "Dinner", new TimeSpan(19, 0, 0), order: 0);
            AddDish(db, dinner, "Beef", "Main", order: 0, tags: null);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEventNights.Remove(
                await db.HostedEventNights.FirstAsync(n => n.Id == seeded.SaturdayId));
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            Assert.Empty(await db.HostedEventMenus.ToListAsync());
            Assert.Empty(await db.HostedEventMenuItems.ToListAsync());
        }
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    private static HostedEventMenu AddSitting(
        Ben.Data.Source.Context.BenDataContext db, Guid nightId, string title,
        TimeSpan servedAt, int order)
    {
        var menu = new HostedEventMenu
        {
            Id = Guid.NewGuid(), HostedEventNightId = nightId, Title = title,
            ServedAtLocal = servedAt, SortOrder = order,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventMenus.Add(menu);
        return menu;
    }

    private static void AddDish(
        Ben.Data.Source.Context.BenDataContext db, HostedEventMenu menu, string name,
        string course, int order, string? tags)
        => db.HostedEventMenuItems.Add(new HostedEventMenuItem
        {
            Id = Guid.NewGuid(), HostedEventMenuId = menu.Id, Name = name, Course = course,
            DietaryTags = tags, SortOrder = order, DateCreated = DateTime.UtcNow,
        });

    private static async Task<Seeded> SeedAsync(SqliteTestDb sqlite)
    {
        await using var db = await sqlite.NewContextAsync();

        db.Users.Add(new AppUser
        {
            Id = HostId, Email = $"{HostId:N}@example.com", UserName = $"{HostId:N}@example.com",
            DisplayName = "The Host", DateCreated = DateTime.UtcNow,
        });
        db.Organizations.Add(new Organization
        {
            Id = OrgId, Name = "The Thomas House", UrlName = "thomas-house",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.Places.Add(new Place
        {
            Id = PlaceId, Name = "The Thomas House Hotel",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });
        db.HostedEvents.Add(new HostedEvent
        {
            Id = EventId, OrganizationId = OrgId, PlaceId = PlaceId,
            Name = "Halloween Lock-In", UrlName = "halloween-lock-in",
            StartsOn = Friday, EndsOn = Saturday, IsPublished = true,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        });

        var friday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = Friday,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        var saturday = new HostedEventNight
        {
            Id = Guid.NewGuid(), HostedEventId = EventId, Date = Saturday,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = HostId,
        };
        db.HostedEventNights.AddRange(friday, saturday);

        await db.SaveChangesAsync();
        return new Seeded(friday.Id, saturday.Id);
    }
}
