using Ben.Data.Common.Enums;
using Ben.Data.Common.Menus;

namespace Ben.Service.Models.Entities;

/// <summary>
/// Finished menus to show what each design looks like (Ben, 10/13/2026: "Create demo menus using
/// each design to show an end user what it looks like when done").
/// </summary>
/// <remarks>
/// <para>The ghost-hunting designs get a haunted supper and the general ones a classic dinner, so each
/// look is shown with food that suits it. Both mix the three ways an item can be paid for (in the
/// meal's price, covered by the event, priced on its own), so the sample shows how each prints.</para>
///
/// <para>Invented, never a real event: the names are obviously samples, and nothing here is a row
/// in the database. The design picker, the demo event and the PDF samples all draw these.</para>
/// </remarks>
public static class MenuSamples
{
    private static readonly DateTime Friday = new(2026, 10, 30);

    public static EventMenuRecord Dinner(string designKey)
        => MenuDesigns.FindOrDefault(designKey).Family == MenuDesigns.GhostHunting ? Haunted(designKey) : Classic(designKey);

    /// <summary>What's on sale the whole event: candy, water and soft drinks.</summary>
    public static EventMenuRecord Concessions(string designKey)
        => Menu(designKey, "Snack table", "On offer all weekend, at the table by the stairs.", MenuMeal.Snacks, throughout: true,
            prices: [],
            sections:
            [
                Section("Candy", [
                    Priced("Full-size candy bars", "Assorted.", 2m),
                    Priced("Gummy ghosts", "A bag.", 1.5m, MenuDietary.Vegan),
                ]),
                Section("Soft Drinks", [
                    Event("Bottled water", "As much as you like."),
                    Priced("Soft drinks", "Cans, from the cooler.", 1.5m),
                    Priced("Energy drinks", "For the 3 a.m. session.", 3m),
                ]),
            ]);

    private static EventMenuRecord Haunted(string designKey)
        => Menu(designKey, "The Séance Supper", "Served in the long dining room before the lights go out.", MenuMeal.Dinner, throughout: false,
            prices: [new("Adults", 38m), new("Children under 12", 16m), new("First responders", 32m)],
            sections:
            [
                Section("Appetizers", [
                    Meal("Graveyard dirt dip", "Black bean and chipotle, with blue corn chips.", MenuDietary.Vegan | MenuDietary.GlutenFree),
                    Meal("Deviled “eyeballs”", "Deviled eggs with an olive stare.", MenuDietary.Vegetarian | MenuDietary.GlutenFree, MenuAllergen.Eggs),
                ]),
                Section("Soups", [
                    Meal("Roasted pumpkin bisque", "Sage, nutmeg and toasted seeds.", MenuDietary.Vegan | MenuDietary.GlutenFree),
                ]),
                Section("Main Course", [
                    Meal("Bourbon-glazed pork chop", "Smoked apples, mashed sweet potato.", MenuDietary.GlutenFree),
                    Meal("Wild mushroom risotto", "Thyme and aged parmesan.", MenuDietary.Vegetarian | MenuDietary.GlutenFree, MenuAllergen.Milk),
                    Meal("Blackened catfish", "Cajun spice, dirty rice.", MenuDietary.Spicy, MenuAllergen.Fish),
                ]),
                Section("Desserts", [
                    Meal("Midnight chocolate cake", "Dark as the cellar.", MenuDietary.Vegetarian, MenuAllergen.Milk | MenuAllergen.Eggs | MenuAllergen.Wheat),
                    Meal("Apple crumble", "Warm, with cinnamon cream.", MenuDietary.Vegetarian, MenuAllergen.Milk | MenuAllergen.Wheat | MenuAllergen.TreeNuts),
                ]),
                Section("Drinks", [
                    Event("Hot spiced cider", "Bottomless, all night."),
                    Priced("“Spirits of the night” cocktail", "Black vodka, cranberry and lime.", 9m, note: "21 and over, with ID"),
                    Priced("Local red or white wine", "By the glass.", 8m),
                ]),
            ]);

    private static EventMenuRecord Classic(string designKey)
        => Menu(designKey, "Dinner", "A three-course dinner, served family style.", MenuMeal.Dinner, throughout: false,
            prices: [new("Adults", 32m), new("Children under 12", 14m), new("Seniors", 28m)],
            sections:
            [
                Section("Appetizers", [
                    Meal("Bruschetta", "Tomato, basil and garlic on grilled bread.", MenuDietary.Vegan, MenuAllergen.Wheat),
                    Meal("Spinach and artichoke dip", "With warm pita.", MenuDietary.Vegetarian, MenuAllergen.Milk | MenuAllergen.Wheat),
                ]),
                Section("Salads", [
                    Meal("Garden salad", "Mixed greens, cucumber, cherry tomatoes, house vinaigrette.", MenuDietary.Vegan | MenuDietary.GlutenFree),
                ]),
                Section("Main Course", [
                    Meal("Herb-roasted chicken", "Lemon, rosemary, roasted potatoes.", MenuDietary.GlutenFree | MenuDietary.DairyFree),
                    Meal("Grilled salmon", "Dill butter, wild rice.", MenuDietary.GlutenFree, MenuAllergen.Fish | MenuAllergen.Milk),
                    Meal("Eggplant parmesan", "Marinara and mozzarella.", MenuDietary.Vegetarian, MenuAllergen.Milk | MenuAllergen.Wheat | MenuAllergen.Eggs),
                ]),
                Section("Add-ons", [
                    Priced("Blue cheese crumble", "For the salad or the chicken.", 2m, allergens: MenuAllergen.Milk),
                    Priced("Extra side of potatoes", null, 4m, MenuDietary.Vegan | MenuDietary.GlutenFree),
                ]),
                Section("Desserts", [
                    Meal("New York cheesecake", "Strawberry sauce.", MenuDietary.Vegetarian, MenuAllergen.Milk | MenuAllergen.Eggs | MenuAllergen.Wheat),
                ]),
                Section("Drinks", [
                    Event("Iced tea and lemonade", "Refilled at the table."),
                    Event("Coffee and hot tea", null),
                    Priced("Craft beer", "Ask about tonight's taps.", 6m, allergens: MenuAllergen.Wheat),
                ]),
            ]);

    // ── building blocks ──────────────────────────────────────────────────────

    private static EventMenuEntryRecord Entry(string name, string? description, MenuPricing pricing, decimal? price,
        MenuDietary dietary, MenuAllergen allergens, string? note)
    {
        var id = Guid.NewGuid();
        return new EventMenuEntryRecord(id, id, pricing, price, note,
            new MenuLibraryItemRecord(id, MenuItemScope.Event, Guid.Empty, "Sample", null, null, Guid.Empty, "",
                name, description, null, dietary, allergens, null, false, null));
    }

    private static EventMenuEntryRecord Meal(string name, string? description, MenuDietary dietary = MenuDietary.None,
        MenuAllergen allergens = MenuAllergen.None)
        => Entry(name, description, MenuPricing.IncludedInMeal, null, dietary, allergens, null);

    private static EventMenuEntryRecord Event(string name, string? description)
        => Entry(name, description, MenuPricing.IncludedInEvent, null, MenuDietary.None, MenuAllergen.None, null);

    private static EventMenuEntryRecord Priced(string name, string? description, decimal price,
        MenuDietary dietary = MenuDietary.None, MenuAllergen allergens = MenuAllergen.None, string? note = null)
        => Entry(name, description, MenuPricing.PricedSeparately, price, dietary, allergens, note);

    private static EventMenuSectionRecord Section(string title, IReadOnlyList<EventMenuEntryRecord> entries)
        => new(Guid.NewGuid(), Guid.Empty, title, null, title, entries);

    private static EventMenuRecord Menu(string designKey, string heading, string intro, MenuMeal meal, bool throughout,
        IReadOnlyList<EventMenuPriceRecord> prices, IReadOnlyList<EventMenuSectionRecord> sections)
        => new(Guid.NewGuid(), Guid.Empty, heading, heading, heading, intro, meal, throughout,
               MenuDesigns.FindOrDefault(designKey).Key, null,
               prices.Count > 0 ? "Gratuity not included." : null, DateTime.UtcNow, prices, sections,
               throughout ? [] : [new EventMenuMealRecord(Guid.Empty, Friday, null, "Dinner", new TimeSpan(19, 0, 0))],
               "Halloween Lock-In at the Thomas House", "The Thomas House Hotel", null);
}
