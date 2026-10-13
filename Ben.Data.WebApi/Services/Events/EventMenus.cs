using Ben.Data.Common.Enums;
using Ben.Data.Common.Menus;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The rules of designed menus (Ben, 10/13/2026), in one place for the builder, the guest pages,
/// the PDF and the event copy.
/// </summary>
public static class EventMenus
{
    // ── which items an event can use ─────────────────────────────────────────

    /// <summary>
    /// The items a planner of this event can put on its menus: this event's own, the group's, and
    /// the venue's (whichever group saved them). Retired ones are left out.
    /// </summary>
    public static IQueryable<MenuLibraryItem> AvailableTo(BenDataContext db, HostedEvent hosted)
        => db.MenuLibraryItems.Where(i => i.RetiredUtc == null
            && ((i.Scope == MenuItemScope.Event && i.HostedEventId == hosted.Id)
             || (i.Scope == MenuItemScope.Group && i.OrganizationId == hosted.OrganizationId)
             || (i.Scope == MenuItemScope.Venue && i.PlaceId == hosted.PlaceId)));

    /// <summary>
    /// Whether the group holding this event may change the item: it made it, or it is the venue
    /// the item is kept at.
    /// </summary>
    public static bool MayEdit(MenuLibraryItem item, Guid orgId, Guid? venueOrgId)
        => item.OrganizationId == orgId
        || (item.Scope == MenuItemScope.Venue && venueOrgId == orgId);

    /// <summary>The verified venue group at a place, if it has one.</summary>
    public static async Task<Guid?> VenueOrgAsync(BenDataContext db, Guid placeId, CancellationToken ct)
        => (await VenueGrants.VerifiedVenueAtAsync(db, placeId, ct))?.OrganizationId;

    // ── reading ──────────────────────────────────────────────────────────────

    public static MenuLibraryItemRecord ItemRecord(MenuLibraryItem i, Guid orgId, Guid? venueOrgId)
        => new(i.Id, i.Scope, i.OrganizationId, i.Organization?.Name ?? "A group", i.HostedEventId, i.PlaceId,
               i.MenuCategoryId, i.MenuCategory?.Name ?? "", i.Name, i.Description, i.ImageUploadFileId,
               i.Dietary, i.Allergens, i.DietaryNote, MayEdit(i, orgId, venueOrgId), i.RetiredUtc);

    /// <summary>The meals of an event, in the order the dining tables use.</summary>
    public static async Task<List<EventMenuMealRecord>> MealsAsync(BenDataContext db, Guid eventId, CancellationToken ct)
        => await db.HostedEventMenus.AsNoTracking()
            .Where(m => m.HostedEventNight.HostedEventId == eventId)
            .OrderBy(m => m.HostedEventNight.Date).ThenBy(m => m.SortOrder)
            .Select(m => new EventMenuMealRecord(m.Id, m.HostedEventNight.Date, m.HostedEventNight.Title, m.Title, m.ServedAtLocal))
            .ToListAsync(ct);

    /// <summary>The menus loaded whole, ready to be turned into records.</summary>
    public static IQueryable<EventMenu> Whole(BenDataContext db)
        => db.EventMenus.AsNoTracking()
            .Include(m => m.HostedEvent).ThenInclude(e => e.Place)
            .Include(m => m.Prices)
            .Include(m => m.Servings).ThenInclude(s => s.Meal).ThenInclude(meal => meal.HostedEventNight)
            .Include(m => m.Sections).ThenInclude(s => s.MenuCategory)
            .Include(m => m.Sections).ThenInclude(s => s.Entries).ThenInclude(e => e.MenuLibraryItem).ThenInclude(i => i.MenuCategory)
            .Include(m => m.Sections).ThenInclude(s => s.Entries).ThenInclude(e => e.MenuLibraryItem).ThenInclude(i => i.Organization)
            .AsSplitQuery();

    /// <summary>What guests read at the top: the planner's heading, the meal, or simply "Menu".</summary>
    public static string ShownHeading(EventMenu m)
        => m.Heading is { Length: > 0 } h ? h
         : m.ThroughoutTheEvent && m.Meal is null ? "Throughout the event"
         : m.Meal is { } meal ? MealName(meal)
         : "Menu";

    public static string MealName(MenuMeal meal) => meal switch
    {
        MenuMeal.Breakfast => "Breakfast",
        MenuMeal.Brunch => "Brunch",
        MenuMeal.Lunch => "Lunch",
        MenuMeal.Supper => "Supper",
        MenuMeal.Dinner => "Dinner",
        MenuMeal.Snacks => "Snacks",
        MenuMeal.Drinks => "Drinks",
        MenuMeal.Dessert => "Dessert",
        _ => meal.ToString(),
    };

    public static EventMenuRecord Record(EventMenu m, Guid viewerOrgId, Guid? venueOrgId)
        => new(
            m.Id, m.HostedEventId, m.Title, m.Heading, ShownHeading(m), m.Intro, m.Meal, m.ThroughoutTheEvent,
            MenuDesigns.FindOrDefault(m.DesignKey).Key, m.LogoUploadFileId, m.PriceNote, m.PublishedUtc,
            [.. m.Prices.OrderBy(p => p.SortOrder).Select(p => new EventMenuPriceRecord(p.Label, p.Amount))],
            [.. m.Sections.OrderBy(s => s.SortOrder).Select(s => new EventMenuSectionRecord(
                s.Id, s.MenuCategoryId, s.MenuCategory.Name, s.Heading,
                s.Heading is { Length: > 0 } h ? h : s.MenuCategory.Name,
                [.. s.Entries.OrderBy(e => e.SortOrder).Select(e => new EventMenuEntryRecord(
                    e.Id, e.MenuLibraryItemId, e.Pricing, e.Price, e.Note,
                    ItemRecord(e.MenuLibraryItem, viewerOrgId, venueOrgId)))]))],
            m.ThroughoutTheEvent ? [] : [.. m.Servings
                .OrderBy(s => s.Meal.HostedEventNight.Date).ThenBy(s => s.Meal.SortOrder)
                .Select(s => new EventMenuMealRecord(s.HostedEventMenuId, s.Meal.HostedEventNight.Date,
                    s.Meal.HostedEventNight.Title, s.Meal.Title, s.Meal.ServedAtLocal))],
            m.HostedEvent.Name, m.HostedEvent.Place?.Name, m.DateUpdated);

    public static EventMenuSummaryRecord Summary(EventMenuRecord r)
        => new(r.Id, r.Title, r.ShownHeading, r.Meal, r.ThroughoutTheEvent, r.DesignKey, r.PublishedUtc,
               r.Sections.Sum(s => s.Entries.Count), r.Meals, r.DateUpdated);

    /// <summary>
    /// An event's menus in the order a guest reads them: by the first meal each is served at, then
    /// the menus offered throughout, then (for the planner) the ones not placed yet.
    /// </summary>
    public static IEnumerable<EventMenuRecord> InReadingOrder(IEnumerable<EventMenuRecord> menus)
        => menus
            .OrderBy(m => m.Meals.Count > 0 ? 0 : m.ThroughoutTheEvent ? 1 : 2)
            .ThenBy(m => m.Meals.Count > 0 ? m.Meals[0].NightDate : DateTime.MaxValue)
            .ThenBy(m => m.Meals.Count > 0 ? m.Meals[0].ServedAtLocal ?? TimeSpan.MaxValue : TimeSpan.MaxValue)
            .ThenBy(m => m.ShownHeading, StringComparer.OrdinalIgnoreCase);

    // ── saving ───────────────────────────────────────────────────────────────

    /// <summary>
    /// What is wrong with a menu as sent, in a sentence, or null. Checked before anything is
    /// written, so a refused save changes nothing.
    /// </summary>
    public static string? WhyItCannotBeSaved(
        SaveEventMenuRequest r, ISet<Guid> usableItems, ISet<Guid> categories, ISet<Guid> meals)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) return "Give the menu a name, so you can tell it from the others.";
        if (r.Title.Trim().Length > 120) return "The menu's name can be at most 120 characters.";
        if (r.Heading?.Trim().Length > 120) return "The heading can be at most 120 characters.";
        if (r.Intro?.Trim().Length > 500) return "The line under the heading can be at most 500 characters.";
        if (r.PriceNote?.Trim().Length > 300) return "The note under the prices can be at most 300 characters.";
        if (MenuDesigns.Find(r.DesignKey) is null) return "Choose one of the designs.";
        if (r.Meal is { } meal && !Enum.IsDefined(meal)) return "Choose one of the meals.";

        foreach (var p in r.Prices)
        {
            if (p.Amount < 0) return "A price can't be less than zero.";
            if (p.Label?.Trim().Length > 80) return "A price's label can be at most 80 characters.";
        }
        if (r.Prices.Count(p => string.IsNullOrWhiteSpace(p.Label)) > 1)
            return "Only one price can be for everybody. Give the others a label, such as Children or Seniors.";

        foreach (var s in r.Sections)
        {
            if (!categories.Contains(s.MenuCategoryId)) return "One of the sections is under a category that no longer exists.";
            if (s.Heading?.Trim().Length > 80) return "A section heading can be at most 80 characters.";
            foreach (var e in s.Entries)
            {
                if (!usableItems.Contains(e.MenuLibraryItemId))
                    return "One of the items isn't available to this event any more. Take it off the menu and save again.";
                if (!Enum.IsDefined(e.Pricing)) return "Say how each item is paid for.";
                if (e.Pricing == MenuPricing.PricedSeparately && e.Price is null)
                    return "An item priced on its own needs its price.";
                if (e.Price is < 0) return "A price can't be less than zero.";
                if (e.Note?.Trim().Length > 200) return "A note on an item can be at most 200 characters.";
            }
        }

        if (!r.ThroughoutTheEvent && r.MealIds.Any(id => !meals.Contains(id)))
            return "One of the meals isn't part of this event any more.";

        return null;
    }

    /// <summary>Writes a checked request onto a loaded menu, replacing its prices, sections and meals.</summary>
    /// <remarks>
    /// The new rows are added to the context by name, not just to the loaded menu's collections:
    /// a row with its id already set, put into a tracked parent's list, is taken for one that exists,
    /// and the save becomes an update that matches nothing.
    /// </remarks>
    public static void Apply(BenDataContext db, EventMenu menu, SaveEventMenuRequest r, Guid userId, DateTime now)
    {
        menu.Title = r.Title.Trim();
        menu.Heading = Trimmed(r.Heading);
        menu.Intro = Trimmed(r.Intro);
        menu.Meal = r.Meal;
        menu.ThroughoutTheEvent = r.ThroughoutTheEvent;
        menu.DesignKey = r.DesignKey;
        menu.PriceNote = Trimmed(r.PriceNote);
        menu.DateUpdated = now;
        menu.UpdatedByAppUserId = userId;

        db.EventMenuPrices.RemoveRange(menu.Prices);
        menu.Prices.Clear();
        var order = 0;
        foreach (var p in r.Prices)
        {
            var price = new EventMenuPrice
            {
                Id = Guid.NewGuid(), EventMenuId = menu.Id, Label = Trimmed(p.Label), Amount = p.Amount, SortOrder = order++,
            };
            db.EventMenuPrices.Add(price);
            menu.Prices.Add(price);
        }

        db.EventMenuEntries.RemoveRange(menu.Sections.SelectMany(s => s.Entries));
        db.EventMenuSections.RemoveRange(menu.Sections);
        menu.Sections.Clear();
        order = 0;
        foreach (var s in r.Sections)
        {
            var section = new EventMenuSection
            {
                Id = Guid.NewGuid(), EventMenuId = menu.Id, MenuCategoryId = s.MenuCategoryId,
                Heading = Trimmed(s.Heading), SortOrder = order++,
            };
            db.EventMenuSections.Add(section);
            var at = 0;
            foreach (var e in s.Entries)
            {
                var entry = new EventMenuEntry
                {
                    Id = Guid.NewGuid(), EventMenuSectionId = section.Id, MenuLibraryItemId = e.MenuLibraryItemId,
                    Pricing = e.Pricing,
                    // A price only means something when the item is paid for on its own.
                    Price = e.Pricing == MenuPricing.PricedSeparately ? e.Price : null,
                    Note = Trimmed(e.Note), SortOrder = at++,
                };
                db.EventMenuEntries.Add(entry);
                section.Entries.Add(entry);
            }
            menu.Sections.Add(section);
        }

        db.EventMenuServings.RemoveRange(menu.Servings);
        menu.Servings.Clear();
        if (!r.ThroughoutTheEvent)
            foreach (var mealId in r.MealIds.Distinct())
            {
                var serving = new EventMenuServing { EventMenuId = menu.Id, HostedEventMenuId = mealId };
                db.EventMenuServings.Add(serving);
                menu.Servings.Add(serving);
            }
    }

    /// <summary>
    /// Why a menu can't be shown to guests yet, in a sentence, or null when it can.
    /// </summary>
    public static string? WhyItCannotBePublished(EventMenu menu)
    {
        var entries = menu.Sections.SelectMany(s => s.Entries).ToList();
        if (entries.Count == 0) return "Put at least one item on the menu first.";
        if (!menu.ThroughoutTheEvent && menu.Servings.Count == 0)
            return "Choose which meals it's served at, or mark it as on offer throughout the event.";
        if (entries.Any(e => e.Pricing == MenuPricing.IncludedInMeal) && menu.Prices.Count == 0)
            return "Some items are included in the meal's price, so the menu needs that price. Add it under Prices, "
                 + "or mark those items as covered by the event or priced on their own.";
        return null;
    }

    /// <summary>
    /// "Friday dinner Copy 1", numbered upward past any copy that already has the number (Ben, 10/13/2026).
    /// </summary>
    public static string CopyTitle(string original, IEnumerable<string> existing)
    {
        // A copy of a copy is still numbered from the original's name.
        var stem = System.Text.RegularExpressions.Regex.Replace(original.Trim(), @" Copy \d+$", "");
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var n = 1; ; n++)
        {
            var title = $"{stem} Copy {n}";
            if (title.Length > 120) title = $"{stem[..Math.Max(0, 120 - $" Copy {n}".Length)]} Copy {n}";
            if (!taken.Contains(title)) return title;
        }
    }

    /// <summary>A new menu with everything of the original's but its meals and its publishing.</summary>
    public static EventMenu Copy(EventMenu source, string title, Guid hostedEventId, Guid userId, DateTime now,
                                 IReadOnlyDictionary<Guid, Guid>? itemMap = null)
    {
        var copy = new EventMenu
        {
            Id = Guid.NewGuid(), HostedEventId = hostedEventId, Title = title, Heading = source.Heading,
            Intro = source.Intro, Meal = source.Meal, ThroughoutTheEvent = source.ThroughoutTheEvent,
            DesignKey = source.DesignKey, LogoUploadFileId = source.LogoUploadFileId, PriceNote = source.PriceNote,
            CopiedFromEventMenuId = source.Id, DateCreated = now, CreatedByAppUserId = userId,
        };
        foreach (var p in source.Prices.OrderBy(p => p.SortOrder))
            copy.Prices.Add(new EventMenuPrice { Id = Guid.NewGuid(), EventMenuId = copy.Id, Label = p.Label, Amount = p.Amount, SortOrder = p.SortOrder });
        foreach (var s in source.Sections.OrderBy(s => s.SortOrder))
        {
            var section = new EventMenuSection
            {
                Id = Guid.NewGuid(), EventMenuId = copy.Id, MenuCategoryId = s.MenuCategoryId, Heading = s.Heading, SortOrder = s.SortOrder,
            };
            foreach (var e in s.Entries.OrderBy(e => e.SortOrder))
                section.Entries.Add(new EventMenuEntry
                {
                    Id = Guid.NewGuid(), EventMenuSectionId = section.Id,
                    MenuLibraryItemId = itemMap is not null && itemMap.TryGetValue(e.MenuLibraryItemId, out var mapped) ? mapped : e.MenuLibraryItemId,
                    Pricing = e.Pricing, Price = e.Price, Note = e.Note, SortOrder = e.SortOrder,
                });
            copy.Sections.Add(section);
        }
        return copy;
    }

    // ── the library ──────────────────────────────────────────────────────────

    public static string? WhyTheItemCannotBeSaved(SaveMenuLibraryItemRequest r, ISet<Guid> categories)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Give the item a name.";
        if (r.Name.Trim().Length > 200) return "An item's name can be at most 200 characters.";
        if (r.Description?.Trim().Length > 1000) return "A description can be at most 1,000 characters.";
        if (r.DietaryNote?.Trim().Length > 300) return "The note can be at most 300 characters.";
        if (!Enum.IsDefined(r.Scope)) return "Choose where to keep the item.";
        if (!categories.Contains(r.MenuCategoryId)) return "Choose one of the categories.";
        return null;
    }

    // ── categories ───────────────────────────────────────────────────────────

    public static MenuCategoryRecord CategoryRecord(MenuCategory c) => new(c.Id, c.Name, c.SortOrder, c.IsStarter);

    /// <summary>The category by that name, or a new one; never a second with the same name.</summary>
    public static async Task<(MenuCategory Category, bool Existed)> FindOrAddCategoryAsync(
        BenDataContext db, string name, Guid userId, CancellationToken ct)
    {
        var shown = name.Trim();
        var key = MenuCategory.KeyFor(shown);
        var found = await db.MenuCategories.FirstOrDefaultAsync(c => c.NameKey == key, ct);
        if (found is not null) return (found, true);

        var next = (await db.MenuCategories.Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0) + 10;
        var made = new MenuCategory
        {
            Id = Guid.NewGuid(), Name = shown, NameKey = key, SortOrder = next,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.MenuCategories.Add(made);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Somebody else added the same one a moment ago: theirs is the one.
            db.Entry(made).State = EntityState.Detached;
            return (await db.MenuCategories.FirstAsync(c => c.NameKey == key, ct), true);
        }
        return (made, false);
    }

    private static string? Trimmed(string? value) => value?.Trim() is { Length: > 0 } v ? v : null;
}

/// <summary>
/// The published menus in the shape the iPhone app reads (10/13/2026).
/// </summary>
/// <remarks>
/// <para>The app's menus screen predates designed menus: one entry per meal, each with "course" and
/// dishes. Released copies of the app can't change, so this keeps that shape and fills it from what is
/// published now: each menu once for every meal it's served at, sections as courses, and the dietary
/// marks and allergens written out as the tags the app splits on commas. A menu on offer throughout the
/// event is listed on the first night, with no time.</para>
/// </remarks>
public static class EventMenuLegacyShape
{
    public static async Task<HostedEventMenusRecord> ComposeAsync(BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var menus = await Controllers.Public.PublicEventMenuController.PublishedAsync(db, eventId, ct);
        var firstNight = await db.HostedEventNights.AsNoTracking().Where(n => n.HostedEventId == eventId)
            .OrderBy(n => n.Date).Select(n => new { n.Id, n.Date, n.Title }).FirstOrDefaultAsync(ct);
        var nights = await db.HostedEventMenus.AsNoTracking().Where(m => m.HostedEventNight.HostedEventId == eventId)
            .Select(m => new { m.Id, m.HostedEventNightId, m.Notes }).ToDictionaryAsync(m => m.Id, ct);

        var rows = new List<HostedEventMenuRecord>();
        var order = 0;
        foreach (var menu in menus)
        {
            var items = menu.Sections.SelectMany(s => s.Entries.Select(e => (Course: s.Title, Entry: e)))
                .Select((x, i) => new HostedEventMenuItemRecord(
                    x.Entry.Id, x.Course, x.Entry.Item.Name, x.Entry.Item.Description, Tags(x.Entry), i))
                .ToList();
            var notes = string.Join(" ", new[] { menu.Intro, EventMenuPresentation.PriceLine(menu), EventMenuPresentation.IncludedLine(menu) }
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            if (menu.ThroughoutTheEvent)
            {
                if (firstNight is null) continue;
                rows.Add(new HostedEventMenuRecord(menu.Id, firstNight.Id, firstNight.Date, firstNight.Title,
                    $"{menu.ShownHeading} (throughout the event)", null, notes.Length == 0 ? null : notes, order++, items));
                continue;
            }

            foreach (var meal in menu.Meals)
            {
                if (!nights.TryGetValue(meal.HostedEventMenuId, out var at)) continue;
                rows.Add(new HostedEventMenuRecord(
                    menu.Meals.Count == 1 ? menu.Id : Combine(menu.Id, meal.HostedEventMenuId),
                    at.HostedEventNightId, meal.NightDate, meal.NightTitle,
                    menu.ShownHeading == meal.MealTitle ? meal.MealTitle : $"{meal.MealTitle}: {menu.ShownHeading}",
                    meal.ServedAtLocal, notes.Length == 0 ? null : notes, order++, items));
            }
        }

        return new HostedEventMenusRecord(eventId,
            [.. rows.OrderBy(r => r.NightDate).ThenBy(r => r.ServedAtLocal ?? TimeSpan.MaxValue).ThenBy(r => r.SortOrder)]);
    }

    private static string? Tags(EventMenuEntryRecord e)
    {
        var parts = new List<string>();
        foreach (var flag in Enum.GetValues<MenuDietary>().Where(f => f != MenuDietary.None && e.Item.Dietary.HasFlag(f)))
            parts.Add(flag switch
            {
                MenuDietary.GlutenFree => "gluten free",
                MenuDietary.DairyFree => "dairy free",
                MenuDietary.NutFree => "nut free",
                _ => flag.ToString().ToLowerInvariant(),
            });
        if (EventMenuPresentation.AllergenLine(e.Item) is { } contains) parts.Add(contains.TrimEnd('.'));
        if (e.Pricing == MenuPricing.PricedSeparately && e.Price is { } price)
            parts.Add(EventMenuPresentation.Money(price));
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>A stable id for one menu at one meal, so the app's list has one row each.</summary>
    private static Guid Combine(Guid a, Guid b)
    {
        var x = a.ToByteArray();
        var y = b.ToByteArray();
        for (var i = 0; i < 16; i++) x[i] ^= y[i];
        return new Guid(x);
    }
}
