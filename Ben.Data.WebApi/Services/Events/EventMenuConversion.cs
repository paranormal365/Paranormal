using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Turns the dishes typed under a meal into a designed menu served at that meal (10/13/2026).
/// </summary>
/// <remarks>
/// <para>Before designed menus, a meal held its dishes as "Course: Name" lines. Each meal that has
/// any becomes one <see cref="EventMenu"/>, published (those dishes were already shown to the
/// event's guests), attached to that meal, drawn in the plain design, with its dishes made into
/// this-event-only library items. The course becomes the category; a course the site doesn't have
/// yet is added as one, which is what a planner typing it would have got.</para>
///
/// <para><b>The old rows are left as they were.</b> Nothing reads them once a meal has a converted
/// menu, and keeping them means a conversion that went wrong loses nothing.
/// <see cref="EventMenu.ConvertedFromHostedEventMenuId"/> (unique) is what stops a meal being
/// converted twice.</para>
///
/// <para><b>Priced as covered by the event,</b> because the old menus had no prices and were part of
/// what the guests booked.</para>
/// </remarks>
public static class EventMenuConversion
{
    public const string DesignKey = "classic-bistro";

    /// <summary>Converts every meal not yet converted; returns how many menus were made.</summary>
    public static async Task<int> ConvertAsync(BenDataContext db, CancellationToken ct)
    {
        var done = db.EventMenus.Where(m => m.ConvertedFromHostedEventMenuId != null)
                                .Select(m => m.ConvertedFromHostedEventMenuId!.Value);

        var meals = await db.HostedEventMenus
            .Include(m => m.Items)
            .Include(m => m.HostedEventNight).ThenInclude(n => n.HostedEvent)
            .Where(m => m.Items.Any() && !done.Contains(m.Id))
            .ToListAsync(ct);
        if (meals.Count == 0) return 0;

        var categories = await db.MenuCategories.ToListAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var meal in meals)
        {
            var hosted = meal.HostedEventNight.HostedEvent;
            var menu = new EventMenu
            {
                Id = Guid.NewGuid(),
                HostedEventId = hosted.Id,
                Title = Clip($"{meal.Title}, {meal.HostedEventNight.Date:MM/dd/yyyy}", 120),
                Heading = Clip(meal.Title, 120),
                Intro = meal.Notes is { Length: > 0 } notes ? Clip(notes, 500) : null,
                Meal = MealFrom(meal.Title),
                DesignKey = DesignKey,
                PublishedUtc = now,
                ConvertedFromHostedEventMenuId = meal.Id,
                DateCreated = now,
                CreatedByAppUserId = meal.CreatedByAppUserId,
            };
            menu.Servings.Add(new EventMenuServing { EventMenuId = menu.Id, HostedEventMenuId = meal.Id });

            var sections = new Dictionary<Guid, EventMenuSection>();
            foreach (var dish in meal.Items.OrderBy(i => i.SortOrder))
            {
                var category = CategoryFor(dish.Course, categories, db, now);
                if (!sections.TryGetValue(category.Id, out var section))
                {
                    section = new EventMenuSection
                    {
                        Id = Guid.NewGuid(), EventMenuId = menu.Id, MenuCategoryId = category.Id,
                        SortOrder = sections.Count,
                    };
                    sections[category.Id] = section;
                    menu.Sections.Add(section);
                }

                var (dietary, allergens, leftover) = ReadTags(dish.DietaryTags);
                var item = new MenuLibraryItem
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = hosted.OrganizationId,
                    Scope = MenuItemScope.Event,
                    HostedEventId = hosted.Id,
                    MenuCategoryId = category.Id,
                    Name = Clip(dish.Name, 200),
                    Description = dish.Description,
                    Dietary = dietary,
                    Allergens = allergens,
                    DietaryNote = leftover,
                    DateCreated = now,
                    CreatedByAppUserId = meal.CreatedByAppUserId,
                };
                db.MenuLibraryItems.Add(item);

                section.Entries.Add(new EventMenuEntry
                {
                    Id = Guid.NewGuid(), EventMenuSectionId = section.Id, MenuLibraryItemId = item.Id,
                    Pricing = MenuPricing.IncludedInEvent, SortOrder = section.Entries.Count,
                });
            }

            db.EventMenus.Add(menu);
        }

        await db.SaveChangesAsync(ct);
        return meals.Count;
    }

    /// <summary>The meal a sitting's title names, when it names one.</summary>
    public static MenuMeal? MealFrom(string? title)
    {
        var t = (title ?? "").ToLowerInvariant();
        if (t.Contains("brunch")) return MenuMeal.Brunch;
        if (t.Contains("breakfast")) return MenuMeal.Breakfast;
        if (t.Contains("lunch")) return MenuMeal.Lunch;
        if (t.Contains("supper")) return MenuMeal.Supper;
        if (t.Contains("dinner")) return MenuMeal.Dinner;
        if (t.Contains("dessert") || t.Contains("pudding")) return MenuMeal.Dessert;
        if (t.Contains("snack")) return MenuMeal.Snacks;
        if (t.Contains("drink") || t.Contains("bar")) return MenuMeal.Drinks;
        return null;
    }

    /// <summary>The category a typed course means: one the site has, or a new one by that name.</summary>
    private static MenuCategory CategoryFor(string? course, List<MenuCategory> categories, BenDataContext db, DateTime now)
    {
        var name = string.IsNullOrWhiteSpace(course) ? "Dishes" : Clip(course.Trim(), 60);
        var key = MenuCategory.KeyFor(name);

        // The kitchen's own words for the same thing.
        key = key switch
        {
            "starter" or "starters" or "appetizer" or "appetiser" or "appetisers" => "appetizers",
            "main" or "mains" or "entree" or "entrée" => "maincourse",
            "pudding" or "puddings" or "dessert" or "sweet" or "sweets" => "desserts",
            "side" => "sides",
            "drink" => "drinks",
            "snack" => "snacks",
            "soup" => "soups",
            "salad" => "salads",
            _ => key,
        };

        var found = categories.FirstOrDefault(c => c.NameKey == key);
        if (found is not null) return found;

        found = new MenuCategory
        {
            Id = Guid.NewGuid(), Name = name, NameKey = key,
            SortOrder = (categories.Count == 0 ? 0 : categories.Max(c => c.SortOrder)) + 10,
            DateCreated = now,
        };
        categories.Add(found);
        db.MenuCategories.Add(found);
        return found;
    }

    /// <summary>
    /// Reads typed dietary tags into the marks the site knows, keeping what it doesn't as a note.
    /// </summary>
    public static (MenuDietary Dietary, MenuAllergen Allergens, string? Leftover) ReadTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return (MenuDietary.None, MenuAllergen.None, null);

        var dietary = MenuDietary.None;
        var allergens = MenuAllergen.None;
        var leftover = new List<string>();

        foreach (var raw in tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = raw.ToLowerInvariant().Replace("-", " ");
            switch (t)
            {
                case "vegan": dietary |= MenuDietary.Vegan; break;
                case "vegetarian" or "veggie": dietary |= MenuDietary.Vegetarian; break;
                case "gluten free" or "gf": dietary |= MenuDietary.GlutenFree; break;
                case "dairy free" or "df": dietary |= MenuDietary.DairyFree; break;
                case "nut free": dietary |= MenuDietary.NutFree; break;
                case "spicy" or "hot": dietary |= MenuDietary.Spicy; break;
                case "halal": dietary |= MenuDietary.Halal; break;
                case "kosher": dietary |= MenuDietary.Kosher; break;
                case "contains nuts" or "nuts" or "tree nuts": allergens |= MenuAllergen.TreeNuts; break;
                case "contains peanuts" or "peanuts": allergens |= MenuAllergen.Peanuts; break;
                case "contains dairy" or "dairy" or "milk": allergens |= MenuAllergen.Milk; break;
                case "contains eggs" or "eggs" or "egg": allergens |= MenuAllergen.Eggs; break;
                case "contains gluten" or "gluten" or "wheat": allergens |= MenuAllergen.Wheat; break;
                case "contains fish" or "fish": allergens |= MenuAllergen.Fish; break;
                case "contains shellfish" or "shellfish": allergens |= MenuAllergen.Shellfish; break;
                case "contains soy" or "soy": allergens |= MenuAllergen.Soy; break;
                case "contains sesame" or "sesame": allergens |= MenuAllergen.Sesame; break;
                default: leftover.Add(raw); break;
            }
        }

        return (dietary, allergens, leftover.Count == 0 ? null : Clip(string.Join(", ", leftover), 300));
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Runs <see cref="EventMenuConversion"/> once the site starts; idempotent.</summary>
public sealed class EventMenuConversionBackfillService : BackgroundService
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<EventMenuConversionBackfillService> _logger;

    public EventMenuConversionBackfillService(
        IDbContextFactory<BenDataContext> dbFactory, ILogger<EventMenuConversionBackfillService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(stoppingToken);
            var made = await EventMenuConversion.ConvertAsync(db, stoppingToken);
            if (made > 0)
                _logger.LogInformation("Made {Count} designed menu(s) from meals' typed dishes.", made);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the next start picks up where this left off.
        }
        catch (Exception ex)
        {
            // Not fatal: the old dishes are untouched, and the next start tries again.
            _logger.LogError(ex, "Could not convert meals' typed dishes into designed menus.");
        }
    }
}
