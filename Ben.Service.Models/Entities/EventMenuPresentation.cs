using System.Globalization;
using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>
/// The words a menu prints, decided once for the page and the PDF (10/13/2026).
/// </summary>
/// <remarks>
/// <para><b>Prices.</b> A menu-level price ("Adults $25 · Children $12") covers everything included in
/// the meal, so those items carry no price of their own. An item priced on its own carries its price.
/// An item covered by the event says so, but only when the menu mixes it with other kinds: a menu that
/// is all covered by the event says it once, under the heading, rather than "included" on every line.</para>
///
/// <para><b>Marks.</b> Short letters a reader can scan (V, VG, GF…), with a key at the foot of the
/// menu, and allergens said in words, because "contains peanuts" is not something to decode.</para>
/// </remarks>
public static class EventMenuPresentation
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal amount)
        => amount == decimal.Truncate(amount) ? amount.ToString("$#,0", Us) : amount.ToString("$#,0.00", Us);

    /// <summary>"Adults $25 · Children $12", or "$25 per person" for a single unlabeled price.</summary>
    public static string? PriceLine(EventMenuRecord menu)
    {
        if (menu.Prices.Count == 0) return null;
        if (menu.Prices.Count == 1 && string.IsNullOrWhiteSpace(menu.Prices[0].Label))
            return $"{Money(menu.Prices[0].Amount)} per person";
        return string.Join("  ·  ", menu.Prices.Select(p =>
            string.IsNullOrWhiteSpace(p.Label) ? Money(p.Amount) : $"{p.Label} {Money(p.Amount)}"));
    }

    private static IEnumerable<EventMenuEntryRecord> Entries(EventMenuRecord menu) => menu.Sections.SelectMany(s => s.Entries);

    /// <summary>Whether everything on the menu is covered by the event's price.</summary>
    public static bool AllIncludedInEvent(EventMenuRecord menu)
        => Entries(menu).Any() && Entries(menu).All(e => e.Pricing == MenuPricing.IncludedInEvent);

    /// <summary>
    /// What the event's price covers, in words — "Friday breakfast and some of Snack table" — or null
    /// when no menu has anything covered by it.
    /// </summary>
    /// <remarks>
    /// Ben, 10/13/2026: "If the price is included in the event price, it will be notated in the event
    /// ticket information." The event page, the confirmation letter and the pass all say this, from here
    /// (event-scenario-gaps: until then only the event page did).
    /// </remarks>
    public static string? IncludedWithTheEvent(IEnumerable<EventMenuRecord> menus)
    {
        var covered = menus.Where(m => Entries(m).Any(e => e.Pricing == MenuPricing.IncludedInEvent)).ToList();
        if (covered.Count == 0) return null;
        var names = covered.Select(m =>
        {
            var what = m.ThroughoutTheEvent ? m.ShownHeading
                     : m.Meals.Count == 1 ? $"{m.Meals[0].NightDate:dddd} {m.Meals[0].MealTitle.ToLowerInvariant()}"
                     : m.ShownHeading;
            return AllIncludedInEvent(m) ? what : $"some of {what}";
        }).Distinct().ToList();
        return names.Count switch
        {
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }

    /// <summary>The line under the heading for a menu the event pays for, or null.</summary>
    public static string? IncludedLine(EventMenuRecord menu)
        => AllIncludedInEvent(menu) ? "Included with the event" : null;

    /// <summary>What prints at the right of an item: its price, "Included", or nothing.</summary>
    public static string? EntryPrice(EventMenuRecord menu, EventMenuEntryRecord entry) => entry.Pricing switch
    {
        MenuPricing.PricedSeparately when entry.Price is { } price => Money(price),
        MenuPricing.IncludedInEvent when !AllIncludedInEvent(menu) => "Included",
        _ => null,
    };

    private static readonly (MenuDietary Mark, string Letters, string Words)[] DietaryMarks =
    [
        (MenuDietary.Vegan, "VG", "vegan"),
        (MenuDietary.Vegetarian, "V", "vegetarian"),
        (MenuDietary.GlutenFree, "GF", "gluten free"),
        (MenuDietary.DairyFree, "DF", "dairy free"),
        (MenuDietary.NutFree, "NF", "nut free"),
        (MenuDietary.Spicy, "S", "spicy"),
        (MenuDietary.Halal, "H", "halal"),
        (MenuDietary.Kosher, "K", "kosher"),
    ];

    private static readonly (MenuAllergen Mark, string Words)[] AllergenWords =
    [
        (MenuAllergen.Milk, "milk"), (MenuAllergen.Eggs, "eggs"), (MenuAllergen.Fish, "fish"),
        (MenuAllergen.Shellfish, "shellfish"), (MenuAllergen.TreeNuts, "tree nuts"), (MenuAllergen.Peanuts, "peanuts"),
        (MenuAllergen.Wheat, "wheat"), (MenuAllergen.Soy, "soy"), (MenuAllergen.Sesame, "sesame"),
    ];

    /// <summary>"VG GF", for beside an item's name.</summary>
    public static string? Letters(MenuDietary dietary)
    {
        var letters = DietaryMarks.Where(m => dietary.HasFlag(m.Mark)).Select(m => m.Letters).ToList();
        return letters.Count == 0 ? null : string.Join(" ", letters);
    }

    /// <summary>"Contains milk, eggs." with any note after it, for under an item.</summary>
    public static string? AllergenLine(MenuLibraryItemRecord item)
    {
        var words = AllergenWords.Where(a => item.Allergens.HasFlag(a.Mark)).Select(a => a.Words).ToList();
        var contains = words.Count == 0 ? null : $"Contains {string.Join(", ", words)}.";
        return (contains, item.DietaryNote) switch
        {
            (null, null) => null,
            ({ } c, null) => c,
            (null, { } n) => n,
            ({ } c, { } n) => $"{c} {n}",
        };
    }

    /// <summary>The key to the letters used on this menu, or null when none are.</summary>
    public static string? Key(EventMenuRecord menu)
    {
        var used = Entries(menu).Aggregate(MenuDietary.None, (all, e) => all | e.Item.Dietary);
        var parts = DietaryMarks.Where(m => used.HasFlag(m.Mark)).Select(m => $"{m.Letters} {m.Words}").ToList();
        return parts.Count == 0 ? null : string.Join("  ·  ", parts);
    }

    /// <summary>When it is served: "Friday, October 30 · Dinner, 7:00 PM", one line per meal.</summary>
    public static IReadOnlyList<string> WhenLines(EventMenuRecord menu)
    {
        if (menu.ThroughoutTheEvent) return ["Throughout the event"];
        return [.. menu.Meals.Select(m =>
        {
            var day = m.NightDate.ToString("dddd, MMMM d", Us);
            var at = m.ServedAtLocal is { } t ? $", {DateTime.Today.Add(t).ToString("h:mm tt", Us)}" : "";
            return $"{day} · {m.MealTitle}{at}";
        })];
    }
}
