namespace Ben.Data.Common.Enums;

/// <summary>Which meal a menu is for, once the planner says. Null until then (Ben, 10/13/2026).</summary>
/// <remarks>Stored as its number; append only.</remarks>
public enum MenuMeal
{
    Breakfast = 1,
    Brunch = 2,
    Lunch = 3,
    Supper = 4,
    Dinner = 5,
    Snacks = 6,
    Drinks = 7,
    Dessert = 8,
}

/// <summary>Where a menu item is kept, and so which events can use it (Ben, 10/13/2026).</summary>
/// <remarks>
/// Ben: "I know the Thomas House has menus they keep, but it may be the group likes to always bring
/// pizza so it would be the group in that case and the Thomas House in the first."
/// </remarks>
public enum MenuItemScope
{
    /// <summary>This event only.</summary>
    Event = 1,

    /// <summary>Every event the group holds.</summary>
    Group = 2,

    /// <summary>Every event held at this venue, whichever group holds it.</summary>
    Venue = 3,
}

/// <summary>How an item on a menu is paid for.</summary>
public enum MenuPricing
{
    /// <summary>Part of the meal, paid by the menu's meal price.</summary>
    IncludedInMeal = 1,

    /// <summary>Covered by the event's own price, and said so in the event's booking information.</summary>
    IncludedInEvent = 2,

    /// <summary>Paid for on its own, at the price given: an extra, a drink, an add-on.</summary>
    PricedSeparately = 3,
}

/// <summary>What a dish is, for the reader choosing it. Flags, so a dish can be several.</summary>
[Flags]
public enum MenuDietary
{
    None = 0,
    Vegan = 1,
    Vegetarian = 2,
    GlutenFree = 4,
    DairyFree = 8,
    NutFree = 16,
    Spicy = 32,
    Halal = 64,
    Kosher = 128,
    /// <summary>
    /// Alcohol: marked "21+" on the menu, and always priced on its own (Ben, 10/21/2026: "alcohol gets its
    /// own mark and 21+, plus priced separately"). Stored in the same flags, so no column changes.
    /// </summary>
    Alcohol = 256,
}

/// <summary>
/// What a dish contains: the nine major allergens US food labels must name. Flags.
/// </summary>
[Flags]
public enum MenuAllergen
{
    None = 0,
    Milk = 1,
    Eggs = 2,
    Fish = 4,
    Shellfish = 8,
    TreeNuts = 16,
    Peanuts = 32,
    Wheat = 64,
    Soy = 128,
    Sesame = 256,
}
