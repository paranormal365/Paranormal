using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ben.Web.Tests.Services.EventMenus;

/// <summary>
/// The menus that existed before designed menus (10/13/2026): a meal's typed dishes become a
/// published menu served at that meal, once, and nothing about the old rows is lost.
/// </summary>
public sealed class EventMenuConversionTests
{
    private static void Dish(Ben.Data.Source.Context.BenDataContext db, Guid mealId, string name,
                             string? course, int order, string? tags = null, string? description = null)
        => db.HostedEventMenuItems.Add(new HostedEventMenuItem
        {
            Id = Guid.NewGuid(), HostedEventMenuId = mealId, Name = name, Course = course,
            Description = description, DietaryTags = tags, SortOrder = order, DateCreated = DateTime.UtcNow,
        });

    [Fact]
    public async Task A_meals_dishes_become_a_published_menu_served_at_that_meal()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await using (var db = await sqlite.NewContextAsync())
        {
            Dish(db, f.FridayDinnerId, "Pumpkin soup", "Starter", 0, "vegan, gluten free");
            Dish(db, f.FridayDinnerId, "Beef Wellington", "Main", 1, description: "With roast potatoes");
            Dish(db, f.FridayDinnerId, "Treacle tart", "Pudding", 2, "contains nuts, made by Mrs. Hale");
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync())
            Assert.Equal(1, await EventMenuConversion.ConvertAsync(db, default));

        await using (var db = await sqlite.NewContextAsync())
        {
            var menu = await db.EventMenus
                .Include(m => m.Servings)
                .Include(m => m.Sections).ThenInclude(s => s.MenuCategory)
                .Include(m => m.Sections).ThenInclude(s => s.Entries).ThenInclude(e => e.MenuLibraryItem)
                .SingleAsync();

            Assert.True(menu.IsPublished);
            Assert.Equal(MenuMeal.Dinner, menu.Meal);
            Assert.Equal("Dinner", menu.Heading);
            Assert.Equal(f.FridayDinnerId, Assert.Single(menu.Servings).HostedEventMenuId);

            // The kitchen's own words land on the site's categories, in the order they were served.
            Assert.Equal(["Appetizers", "Main Course", "Desserts"],
                menu.Sections.OrderBy(s => s.SortOrder).Select(s => s.MenuCategory.Name).ToArray());

            var items = menu.Sections.SelectMany(s => s.Entries).Select(e => e.MenuLibraryItem).ToList();
            Assert.All(items, i => Assert.Equal(MenuItemScope.Event, i.Scope));
            Assert.All(items, i => Assert.Equal(f.EventId, i.HostedEventId));

            var soup = items.Single(i => i.Name == "Pumpkin soup");
            Assert.Equal(MenuDietary.Vegan | MenuDietary.GlutenFree, soup.Dietary);
            Assert.Equal("With roast potatoes", items.Single(i => i.Name == "Beef Wellington").Description);

            // What the site has a mark for becomes the mark; the rest is kept, word for word.
            var tart = items.Single(i => i.Name == "Treacle tart");
            Assert.Equal(MenuAllergen.TreeNuts, tart.Allergens);
            Assert.Equal("made by Mrs. Hale", tart.DietaryNote);

            Assert.All(menu.Sections.SelectMany(s => s.Entries), e => Assert.Equal(MenuPricing.IncludedInEvent, e.Pricing));

            // The old dishes are still there.
            Assert.Equal(3, await db.HostedEventMenuItems.CountAsync());
        }
    }

    [Fact]
    public async Task Converting_twice_makes_one_menu()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await using (var db = await sqlite.NewContextAsync())
        {
            Dish(db, f.FridayDinnerId, "Soup", null, 0);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync()) await EventMenuConversion.ConvertAsync(db, default);
        await using (var db = await sqlite.NewContextAsync())
            Assert.Equal(0, await EventMenuConversion.ConvertAsync(db, default));

        await using (var db = await sqlite.NewContextAsync())
        {
            Assert.Equal(1, await db.EventMenus.CountAsync());
            Assert.Equal(1, await db.MenuLibraryItems.CountAsync());
        }
    }

    [Fact]
    public async Task A_meal_with_no_dishes_makes_no_menu()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await MenuFixture.SeedAsync(sqlite);

        await using var db = await sqlite.NewContextAsync();
        Assert.Equal(0, await EventMenuConversion.ConvertAsync(db, default));
        Assert.Empty(await db.EventMenus.ToListAsync());
    }

    [Fact]
    public async Task A_course_the_site_has_no_category_for_becomes_one_for_everybody()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        var f = await MenuFixture.SeedAsync(sqlite);
        await using (var db = await sqlite.NewContextAsync())
        {
            Dish(db, f.FridayDinnerId, "Ghost pepper wings", "Spirits of the Grill", 0);
            await db.SaveChangesAsync();
        }

        await using (var db = await sqlite.NewContextAsync()) await EventMenuConversion.ConvertAsync(db, default);

        await using (var db = await sqlite.NewContextAsync())
        {
            var made = await db.MenuCategories.SingleAsync(c => c.Name == "Spirits of the Grill");
            Assert.False(made.IsStarter);
            Assert.Equal(MenuCategorySeeder.Starters.Length + 1, await db.MenuCategories.CountAsync());
        }
    }

    [Fact]
    public async Task The_starter_categories_are_seeded_once_and_a_planners_own_name_is_kept()
    {
        await using var sqlite = await SqliteTestDb.CreateAsync();
        await using (var db = await sqlite.NewContextAsync())
        {
            // A planner who got there first, in their own capitals.
            db.MenuCategories.Add(new MenuCategory
            {
                Id = Guid.NewGuid(), Name = "SOFT DRINKS", NameKey = MenuCategory.KeyFor("SOFT DRINKS"),
                DateCreated = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            await MenuCategorySeeder.SeedAsync(db, default);
            await MenuCategorySeeder.SeedAsync(db, default);
        }

        await using (var db = await sqlite.NewContextAsync())
        {
            Assert.Equal(MenuCategorySeeder.Starters.Length, await db.MenuCategories.CountAsync());
            Assert.Equal("SOFT DRINKS", (await db.MenuCategories.SingleAsync(c => c.NameKey == "softdrinks")).Name);
        }
    }

    [Theory]
    [InlineData("Late supper", MenuMeal.Supper)]
    [InlineData("Sunday brunch", MenuMeal.Brunch)]
    [InlineData("Breakfast", MenuMeal.Breakfast)]
    [InlineData("Midnight vigil", null)]
    public void A_sittings_title_names_its_meal_when_it_says_one(string title, MenuMeal? meal)
        => Assert.Equal(meal, EventMenuConversion.MealFrom(title));
}
