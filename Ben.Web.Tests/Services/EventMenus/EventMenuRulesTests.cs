using Ben.Data.Common.Enums;
using Ben.Data.Common.Menus;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services.DesignedMenus;

/// <summary>
/// The rules of designed menus (Ben, 10/13/2026): where items are kept and who sees them, what a
/// menu needs before it's shown, copying, what guests read, and the PDF.
/// </summary>
public sealed class EventMenuRulesTests
{
    private static MenuLibraryItem Item(MenuFixture f, Ben.Data.Source.Context.BenDataContext db, string name,
        MenuItemScope scope, Guid? orgId = null, Guid? eventId = null, Guid? placeId = null)
    {
        var item = new MenuLibraryItem
        {
            Id = Guid.NewGuid(), OrganizationId = orgId ?? f.OrgId, Scope = scope,
            HostedEventId = scope == MenuItemScope.Event ? eventId ?? f.EventId : null,
            PlaceId = scope == MenuItemScope.Venue ? placeId ?? f.PlaceId : null,
            MenuCategoryId = MenuFixture.CategoryId(db, "Dishes"), Name = name,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = f.HostId,
        };
        db.MenuLibraryItems.Add(item);
        return item;
    }

    private static SaveEventMenuRequest Request(Guid categoryId, Guid itemId, MenuPricing pricing = MenuPricing.PricedSeparately,
        decimal? price = 12m, IReadOnlyList<Guid>? meals = null, IReadOnlyList<EventMenuPriceRecord>? prices = null,
        bool throughout = false)
        => new("Friday dinner", null, null, MenuMeal.Dinner, throughout, "classic-bistro", null,
               prices ?? [], [new SaveEventMenuSection(categoryId, null, [new SaveEventMenuEntry(itemId, pricing, price, null)])],
               meals ?? []);

    // ── where items are kept ─────────────────────────────────────────────────

    [Fact]
    public async Task An_event_offers_its_own_items_the_groups_and_the_venues_and_nobody_elses()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        var otherEventId = Guid.NewGuid();

        await using (var db = await sqlite.NewContextAsync())
        {
            db.HostedEvents.Add(new HostedEvent
            {
                Id = otherEventId, OrganizationId = f.OrgId, PlaceId = f.PlaceId, Name = "Another night",
                UrlName = $"other-{otherEventId:N}"[..20], StartsOn = MenuFixture.Friday, EndsOn = MenuFixture.Friday,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = f.HostId,
            });
            Item(f, db, "This event's soup", MenuItemScope.Event);
            Item(f, db, "Our pizza", MenuItemScope.Group);
            Item(f, db, "The hotel's pie", MenuItemScope.Venue, orgId: f.OtherOrgId);
            Item(f, db, "Another event's cake", MenuItemScope.Event, eventId: otherEventId);
            Item(f, db, "Another group's chili", MenuItemScope.Group, orgId: f.OtherOrgId);
            var retired = Item(f, db, "Last year's punch", MenuItemScope.Group);
            retired.RetiredUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var hosted = await db.HostedEvents.SingleAsync(e => e.Id == f.EventId);
            var names = await EventMenus.AvailableTo(db, hosted).Select(i => i.Name).OrderBy(n => n).ToListAsync();
            Assert.Equal(["Our pizza", "The hotel's pie", "This event's soup"], names);
        }
    }

    [Fact]
    public void A_venue_item_can_be_changed_by_the_group_that_saved_it_and_by_the_venue()
    {
        var saver = Guid.NewGuid();
        var venue = Guid.NewGuid();
        var visitor = Guid.NewGuid();
        var item = new MenuLibraryItem { OrganizationId = saver, Scope = MenuItemScope.Venue };

        Assert.True(EventMenus.MayEdit(item, saver, venue));
        Assert.True(EventMenus.MayEdit(item, venue, venue));
        Assert.False(EventMenus.MayEdit(item, visitor, venue));
        Assert.False(EventMenus.MayEdit(new MenuLibraryItem { OrganizationId = saver, Scope = MenuItemScope.Group }, venue, venue));
    }

    // ── saving and publishing ────────────────────────────────────────────────

    [Fact]
    public void An_item_priced_on_its_own_needs_its_price()
    {
        var cat = Guid.NewGuid(); var item = Guid.NewGuid();
        var why = EventMenus.WhyItCannotBeSaved(Request(cat, item, price: null), new HashSet<Guid> { item }, new HashSet<Guid> { cat }, new HashSet<Guid>());
        Assert.Equal("An item priced on its own needs its price.", why);

        Assert.Null(EventMenus.WhyItCannotBeSaved(Request(cat, item, MenuPricing.IncludedInEvent, price: null),
            new HashSet<Guid> { item }, new HashSet<Guid> { cat }, new HashSet<Guid>()));
    }

    [Fact]
    public void An_item_the_event_cant_use_and_a_meal_from_elsewhere_are_refused()
    {
        var cat = Guid.NewGuid(); var item = Guid.NewGuid();
        Assert.NotNull(EventMenus.WhyItCannotBeSaved(Request(cat, item), new HashSet<Guid>(), new HashSet<Guid> { cat }, new HashSet<Guid>()));
        Assert.NotNull(EventMenus.WhyItCannotBeSaved(Request(cat, item, meals: [Guid.NewGuid()]),
            new HashSet<Guid> { item }, new HashSet<Guid> { cat }, new HashSet<Guid>()));
        Assert.NotNull(EventMenus.WhyItCannotBeSaved(Request(cat, item) with { DesignKey = "no-such-look" },
            new HashSet<Guid> { item }, new HashSet<Guid> { cat }, new HashSet<Guid>()));
    }

    [Fact]
    public void Only_one_meal_price_can_be_for_everybody()
    {
        var cat = Guid.NewGuid(); var item = Guid.NewGuid();
        var why = EventMenus.WhyItCannotBeSaved(
            Request(cat, item, prices: [new(null, 25m), new(null, 12m)]),
            new HashSet<Guid> { item }, new HashSet<Guid> { cat }, new HashSet<Guid>());
        Assert.StartsWith("Only one price can be for everybody.", why);
    }

    [Fact]
    public void A_menu_is_shown_only_when_it_has_items_somewhere_to_be_and_its_meal_price()
    {
        var menu = new EventMenu();
        Assert.Equal("Put at least one item on the menu first.", EventMenus.WhyItCannotBePublished(menu));

        var section = new EventMenuSection();
        section.Entries.Add(new EventMenuEntry { Pricing = MenuPricing.IncludedInMeal });
        menu.Sections.Add(section);
        Assert.StartsWith("Choose which meals", EventMenus.WhyItCannotBePublished(menu));

        menu.ThroughoutTheEvent = true;
        Assert.StartsWith("Some items are included in the meal's price", EventMenus.WhyItCannotBePublished(menu));

        menu.Prices.Add(new EventMenuPrice { Amount = 25m });
        Assert.Null(EventMenus.WhyItCannotBePublished(menu));
    }

    [Theory]
    [InlineData("Friday dinner", new string[0], "Friday dinner Copy 1")]
    [InlineData("Friday dinner", new[] { "Friday dinner Copy 1" }, "Friday dinner Copy 2")]
    [InlineData("Friday dinner Copy 1", new[] { "Friday dinner Copy 1" }, "Friday dinner Copy 2")]
    public void A_copy_is_named_after_the_original_with_the_next_number(string original, string[] existing, string expected)
        => Assert.Equal(expected, EventMenus.CopyTitle(original, existing.Append(original)));

    [Fact]
    public async Task Adding_a_category_the_site_has_hands_back_the_one_it_has()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await using var db = await sqlite.NewContextAsync();

        var (same, existed) = await EventMenus.FindOrAddCategoryAsync(db, "  soft-drinks ", f.HostId, default);
        Assert.True(existed);
        Assert.Equal("Soft Drinks", same.Name);

        var (made, wasThere) = await EventMenus.FindOrAddCategoryAsync(db, "Midnight Snacks", f.HostId, default);
        Assert.False(wasThere);
        Assert.Equal("Midnight Snacks", made.Name);
    }

    // ── what guests read ─────────────────────────────────────────────────────

    private static async Task<(Guid Published, Guid Draft)> TwoMenusAsync(SqliteTestDb sqlite, MenuFixture f)
    {
        await using var db = await sqlite.NewContextAsync();
        var item = Item(f, db, "Pumpkin soup", MenuItemScope.Group);
        item.Dietary = MenuDietary.Vegan;
        item.Allergens = MenuAllergen.Sesame;

        EventMenu Menu(string title, bool published, params Guid[] meals)
        {
            var m = new EventMenu
            {
                Id = Guid.NewGuid(), HostedEventId = f.EventId, Title = title, Heading = title, Meal = MenuMeal.Dinner,
                DesignKey = "seance-parlor", PublishedUtc = published ? DateTime.UtcNow : null,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = f.HostId,
            };
            var s = new EventMenuSection { Id = Guid.NewGuid(), EventMenuId = m.Id, MenuCategoryId = MenuFixture.CategoryId(db, "Soups") };
            s.Entries.Add(new EventMenuEntry { Id = Guid.NewGuid(), EventMenuSectionId = s.Id, MenuLibraryItemId = item.Id, Pricing = MenuPricing.PricedSeparately, Price = 6.5m });
            m.Sections.Add(s);
            foreach (var meal in meals) m.Servings.Add(new EventMenuServing { EventMenuId = m.Id, HostedEventMenuId = meal });
            db.EventMenus.Add(m);
            return m;
        }

        var published = Menu("The Séance Supper", true, f.FridayDinnerId, f.SaturdayDinnerId);
        var draft = Menu("Still deciding", false, f.FridayDinnerId);
        await db.SaveChangesAsync();
        return (published.Id, draft.Id);
    }

    [Fact]
    public async Task Guests_see_published_menus_of_a_public_event_and_never_a_draft()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        var (published, _) = await TwoMenusAsync(sqlite, f);
        await using (var db = await sqlite.NewContextAsync())
        {
            (await db.EventMenus.SingleAsync(m => m.Id == published)).Title = "Jim's dinner, third go";
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var menus = await PublicEventMenuController.PublishedAsync(db, f.EventId, default);
            var menu = Assert.Single(menus);
            Assert.Equal(published, menu.Id);
            Assert.Equal(2, menu.Meals.Count);
            // The planner's own title is the planner's: guests are sent the heading in its place
            // (event-scenario-gaps; Ben's menu rules: titles are "so the event manager will know").
            Assert.Equal(menu.ShownHeading, menu.Title);
            Assert.NotEqual("Jim's dinner, third go", menu.Title);
            Assert.All(menu.Sections.SelectMany(s => s.Entries), e => Assert.False(e.Item.CanEdit));
        }

        // Taken off the public site, the event takes its menus with it.
        await using (var db = await sqlite.NewContextAsync())
        {
            (await db.HostedEvents.SingleAsync(e => e.Id == f.EventId)).LifecycleState = HostedEventLifecycleState.Draft;
            await db.SaveChangesAsync();
            Assert.Empty(await PublicEventMenuController.PublishedAsync(db, f.EventId, default));
        }
    }

    [Fact]
    public async Task The_phone_app_gets_one_row_per_meal_in_the_shape_it_already_reads()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await TwoMenusAsync(sqlite, f);

        await using var db = await sqlite.NewContextAsync();
        var shape = await EventMenuLegacyShape.ComposeAsync(db, f.EventId, default);

        Assert.Equal(2, shape.Menus.Count);
        Assert.Equal([MenuFixture.Friday, MenuFixture.Saturday], shape.Menus.Select(m => m.NightDate).ToArray());
        Assert.Equal(2, shape.Menus.Select(m => m.Id).Distinct().Count());
        var dish = Assert.Single(shape.Menus[0].Items);
        Assert.Equal("Soups", dish.Course);
        Assert.Equal("vegan, Contains sesame, $6.50", dish.DietaryTags);
    }

    // ── the PDF ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_design_makes_a_pdf()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await TwoMenusAsync(sqlite, f);

        await using var db = await sqlite.NewContextAsync();
        var menu = Assert.Single(await PublicEventMenuController.PublishedAsync(db, f.EventId, default));

        foreach (var design in MenuDesigns.All)
        {
            var bytes = await EventMenuPdf.RenderAsync(menu with { DesignKey = design.Key }, _ => Task.FromResult<byte[]?>(null), 2026);
            Assert.True(bytes.Length > 1000, design.Key);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
    }

    [Fact]
    public async Task A_long_menu_runs_onto_more_pages_rather_than_off_the_bottom()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await TwoMenusAsync(sqlite, f);

        await using var db = await sqlite.NewContextAsync();
        var menu = Assert.Single(await PublicEventMenuController.PublishedAsync(db, f.EventId, default));
        var entry = menu.Sections[0].Entries[0];
        var long80 = menu with
        {
            Sections = [menu.Sections[0] with { Entries = [.. Enumerable.Repeat(entry, 80)] }],
        };

        var bytes = await EventMenuPdf.RenderAsync(long80, _ => Task.FromResult<byte[]?>(null), 2026);
        var pages = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/Type\s*/Page[^s]").Count;
        Assert.True(pages >= 2, $"{pages} page(s)");
    }
}

/// <summary>
/// Writes a sample menu in every design to <c>BEN_MENU_PDF_DIR</c>, for looking at them. Does nothing
/// unless that is set.
/// </summary>
public sealed class EventMenuPdfSamples
{
    [Fact]
    public async Task Write_a_sample_in_every_design_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("BEN_MENU_PDF_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        foreach (var design in MenuDesigns.All)
        {
            var bytes = await EventMenuPdf.RenderAsync(MenuSamples.Dinner(design.Key), _ => Task.FromResult<byte[]?>(null), 2026);
            await File.WriteAllBytesAsync(Path.Combine(dir, $"{design.Key}.pdf"), bytes);
        }
    }
}

/// <summary>Saving a menu through the same code the builder uses, against a real database.</summary>
public sealed class EventMenuSaveTests
{
    [Fact]
    public async Task A_menu_saves_and_saves_again_replacing_what_it_had()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        Guid menuId, itemId, categoryId;

        await using (var db = await sqlite.NewContextAsync())
        {
            categoryId = MenuFixture.CategoryId(db, "Desserts");
            var item = new MenuLibraryItem
            {
                Id = Guid.NewGuid(), OrganizationId = f.OrgId, Scope = MenuItemScope.Group, MenuCategoryId = categoryId,
                Name = "Pudding", DateCreated = DateTime.UtcNow, CreatedByAppUserId = f.HostId,
            };
            var menu = new EventMenu
            {
                Id = Guid.NewGuid(), HostedEventId = f.EventId, Title = "Supper", DateCreated = DateTime.UtcNow, CreatedByAppUserId = f.HostId,
            };
            db.AddRange(item, menu);
            await db.SaveChangesAsync();
            (menuId, itemId) = (menu.Id, item.Id);
        }

        SaveEventMenuRequest Request(decimal price, params Guid[] meals) => new(
            "Supper", "The Midnight Feast", null, MenuMeal.Supper, false, "midnight-vigil", null,
            [new("Adults", 20m)],
            [new SaveEventMenuSection(categoryId, null, [new SaveEventMenuEntry(itemId, MenuPricing.PricedSeparately, price, null)])],
            meals);

        foreach (var (price, meals) in new[] { (5m, new[] { f.FridayDinnerId }), (6m, new[] { f.SaturdayDinnerId }) })
        {
            await using var db = await sqlite.NewContextAsync();
            var menu = await db.EventMenus.Include(m => m.Prices).Include(m => m.Servings)
                .Include(m => m.Sections).ThenInclude(s => s.Entries).SingleAsync(m => m.Id == menuId);
            EventMenus.Apply(db, menu, Request(price, meals), f.HostId, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            var menu = await db.EventMenus.Include(m => m.Prices).Include(m => m.Servings)
                .Include(m => m.Sections).ThenInclude(s => s.Entries).SingleAsync(m => m.Id == menuId);
            Assert.Equal("midnight-vigil", menu.DesignKey);
            Assert.Single(menu.Prices);
            Assert.Equal(6m, Assert.Single(Assert.Single(menu.Sections).Entries).Price);
            Assert.Equal(f.SaturdayDinnerId, Assert.Single(menu.Servings).HostedEventMenuId);
        }
    }
}

/// <summary>The menu fonts (10/13/2026): every design names faces that exist, and every file is there.</summary>
public sealed class MenuFontsTests
{
    [Fact]
    public void Every_design_is_set_in_fonts_the_site_has()
    {
        foreach (var design in MenuDesigns.All)
        {
            Assert.NotNull(MenuFonts.Closest(design.HeadingFace!, design.HeadingWeight, false));
            Assert.NotNull(MenuFonts.Closest(design.BodyFace!, 400, false));
        }
    }

    [Fact]
    public void Every_font_file_is_embedded_and_is_truetype()
    {
        foreach (var file in MenuFonts.Files)
        {
            var bytes = MenuFonts.Read(file.File);
            Assert.True(bytes is { Length: > 10_000 }, file.File);
            // A TrueType file opens with 0x00010000.
            Assert.Equal(new byte[] { 0, 1, 0, 0 }, bytes![..4]);
        }
        Assert.Null(MenuFonts.Read("../../secrets.ttf"));
    }

    [Fact]
    public void A_face_without_the_cut_asked_for_gives_the_one_it_has()
    {
        Assert.Equal("PirataOne-Regular.ttf", MenuFonts.Closest("Pirata One", 700, false)!.File);
        Assert.Equal("Cinzel-Regular.ttf", MenuFonts.Closest("Cinzel", 400, true)!.File);
        Assert.Equal("Lora-Italic.ttf", MenuFonts.Closest("Lora", 400, true)!.File);
        Assert.Null(MenuFonts.Closest("Comic Sans", 400, false));
    }

    [Fact]
    public void The_page_gets_a_rule_for_every_file()
    {
        var css = MenuFonts.FontFaceCss("/menu-fonts/");
        Assert.Equal(MenuFonts.Files.Count, System.Text.RegularExpressions.Regex.Matches(css, "@font-face").Count);
        Assert.Contains("url('/menu-fonts/Cinzel-Bold.ttf')", css);
    }
}
