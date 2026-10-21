using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Kit.Menus;

/// <summary>How the menu screens name things (10/13/2026), in one place so they agree.</summary>
public static class MenuWords
{
    public static readonly (MenuMeal Meal, string Name)[] Meals =
    [
        (MenuMeal.Breakfast, "Breakfast"), (MenuMeal.Brunch, "Brunch"), (MenuMeal.Lunch, "Lunch"),
        (MenuMeal.Supper, "Supper"), (MenuMeal.Dinner, "Dinner"), (MenuMeal.Snacks, "Snacks"),
        (MenuMeal.Drinks, "Drinks"), (MenuMeal.Dessert, "Dessert"),
    ];

    public static string MealName(MenuMeal? meal) => meal is { } m ? Meals.First(x => x.Meal == m).Name : "No meal chosen";

    public static readonly (MenuPricing Pricing, string Name, string What)[] Pricings =
    [
        (MenuPricing.IncludedInMeal, "In the meal's price", "Covered by the menu's price, such as Adults $25."),
        (MenuPricing.IncludedInEvent, "Covered by the event", "Part of what guests paid for the event. The event's booking information says so."),
        (MenuPricing.PricedSeparately, "Priced on its own", "An extra or an add-on, such as alcohol or blue cheese on a steak. Give its price."),
    ];

    public static string PricingName(MenuPricing pricing) => Pricings.First(p => p.Pricing == pricing).Name;

    /// <summary>Where an item is kept, with what that means, for the item form (Ben, 10/13/2026).</summary>
    public static readonly (MenuItemScope Scope, string Name, string What)[] Scopes =
    [
        (MenuItemScope.Event, "Just this event", "Only this event's menus can use it."),
        (MenuItemScope.Group, "Your group", "Every event your group holds can use it, wherever it is. Good for what you always bring, like your own pizza order."),
        (MenuItemScope.Venue, "This venue", "Every event held at this venue can use it, whichever group holds it. Good for what the venue's own kitchen serves. The venue can change it too."),
    ];

    public static string ScopeName(MenuItemScope scope) => Scopes.First(s => s.Scope == scope).Name;

    public static readonly (MenuDietary Mark, string Name)[] Dietary =
    [
        (MenuDietary.Vegan, "Vegan"), (MenuDietary.Vegetarian, "Vegetarian"), (MenuDietary.GlutenFree, "Gluten free"),
        (MenuDietary.DairyFree, "Dairy free"), (MenuDietary.NutFree, "Nut free"), (MenuDietary.Spicy, "Spicy"),
        (MenuDietary.Halal, "Halal"), (MenuDietary.Kosher, "Kosher"),
        (MenuDietary.Alcohol, "Alcohol (21+, always priced on its own)"),
    ];

    public static readonly (MenuAllergen Mark, string Name)[] Allergens =
    [
        (MenuAllergen.Milk, "Milk"), (MenuAllergen.Eggs, "Eggs"), (MenuAllergen.Fish, "Fish"),
        (MenuAllergen.Shellfish, "Shellfish"), (MenuAllergen.TreeNuts, "Tree nuts"), (MenuAllergen.Peanuts, "Peanuts"),
        (MenuAllergen.Wheat, "Wheat"), (MenuAllergen.Soy, "Soy"), (MenuAllergen.Sesame, "Sesame"),
    ];
}
