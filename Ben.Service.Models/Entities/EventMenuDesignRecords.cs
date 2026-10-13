using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

// ── Designed menus (Ben, 10/13/2026) ─────────────────────────────────────────────────────────────
// The wire shapes for categories, the item library and an event's menus. A menu is saved whole,
// sections and entries in the order the planner left them, the same replace-the-set shape the
// meals use: position IS the order.

/// <summary>One heading items sit under, from the site's single list.</summary>
public sealed record MenuCategoryRecord(Guid Id, string Name, int SortOrder, bool IsStarter);

/// <summary>Adding a category the list doesn't have.</summary>
public sealed record AddMenuCategoryRequest(string Name);

/// <summary>
/// The category asked for. <paramref name="AlreadyExisted"/> is true when the list already had it
/// under that name (ignoring case and spacing), in which case that one is returned rather than a
/// second being made.
/// </summary>
public sealed record AddMenuCategoryResult(MenuCategoryRecord Category, bool AlreadyExisted);

/// <summary>One item in the library, as a planner sees it.</summary>
/// <param name="OrganizationName">The group that made it; for a venue item made by another group.</param>
/// <param name="CanEdit">Whether this planner may change or retire it.</param>
public sealed record MenuLibraryItemRecord(
    Guid Id,
    MenuItemScope Scope,
    Guid OrganizationId,
    string OrganizationName,
    Guid? HostedEventId,
    Guid? PlaceId,
    Guid MenuCategoryId,
    string CategoryName,
    string Name,
    string? Description,
    Guid? ImageUploadFileId,
    MenuDietary Dietary,
    MenuAllergen Allergens,
    string? DietaryNote,
    bool CanEdit,
    DateTime? RetiredUtc);

/// <summary>Making or changing a library item.</summary>
public sealed record SaveMenuLibraryItemRequest(
    MenuItemScope Scope,
    Guid MenuCategoryId,
    string Name,
    string? Description,
    MenuDietary Dietary,
    MenuAllergen Allergens,
    string? DietaryNote);

/// <summary>One meal price on a menu.</summary>
public sealed record EventMenuPriceRecord(string? Label, decimal Amount);

/// <summary>One item on a menu, with the item itself.</summary>
public sealed record EventMenuEntryRecord(
    Guid Id, Guid MenuLibraryItemId, MenuPricing Pricing, decimal? Price, string? Note,
    MenuLibraryItemRecord Item);

/// <summary>One section of a menu.</summary>
/// <param name="Title">What the section prints as: its own heading, or the category's name.</param>
public sealed record EventMenuSectionRecord(
    Guid Id, Guid MenuCategoryId, string CategoryName, string? Heading, string Title,
    IReadOnlyList<EventMenuEntryRecord> Entries);

/// <summary>A meal a menu is served at.</summary>
public sealed record EventMenuMealRecord(
    Guid HostedEventMenuId, DateTime NightDate, string? NightTitle, string MealTitle, TimeSpan? ServedAtLocal);

/// <summary>A whole menu: what the builder edits, the preview draws and guests read.</summary>
/// <param name="Title">The planner's own name for it; never shown to guests.</param>
/// <param name="ShownHeading">What guests see at the top: the heading, or the meal's name, or "Menu".</param>
/// <param name="EventName">The event, for the menu's own masthead.</param>
/// <param name="VenueName">Where it is, for the masthead.</param>
public sealed record EventMenuRecord(
    Guid Id,
    Guid HostedEventId,
    string Title,
    string? Heading,
    string ShownHeading,
    string? Intro,
    MenuMeal? Meal,
    bool ThroughoutTheEvent,
    string DesignKey,
    Guid? LogoUploadFileId,
    string? PriceNote,
    DateTime? PublishedUtc,
    IReadOnlyList<EventMenuPriceRecord> Prices,
    IReadOnlyList<EventMenuSectionRecord> Sections,
    IReadOnlyList<EventMenuMealRecord> Meals,
    string EventName,
    string? VenueName,
    DateTime? DateUpdated);

/// <summary>One menu in the event's list.</summary>
public sealed record EventMenuSummaryRecord(
    Guid Id, string Title, string ShownHeading, MenuMeal? Meal, bool ThroughoutTheEvent,
    string DesignKey, DateTime? PublishedUtc, int ItemCount,
    IReadOnlyList<EventMenuMealRecord> Meals, DateTime? DateUpdated);

/// <summary>An entry as the builder sends it back.</summary>
public sealed record SaveEventMenuEntry(Guid MenuLibraryItemId, MenuPricing Pricing, decimal? Price, string? Note);

/// <summary>A section as the builder sends it back.</summary>
public sealed record SaveEventMenuSection(Guid MenuCategoryId, string? Heading, IReadOnlyList<SaveEventMenuEntry> Entries);

/// <summary>A whole menu, saved at once; the order of every list is the order it prints in.</summary>
/// <param name="MealIds">The meals (<c>HostedEventMenu</c> ids) it is served at. Ignored when
/// <paramref name="ThroughoutTheEvent"/> is true.</param>
public sealed record SaveEventMenuRequest(
    string Title,
    string? Heading,
    string? Intro,
    MenuMeal? Meal,
    bool ThroughoutTheEvent,
    string DesignKey,
    string? PriceNote,
    IReadOnlyList<EventMenuPriceRecord> Prices,
    IReadOnlyList<SaveEventMenuSection> Sections,
    IReadOnlyList<Guid> MealIds);

/// <summary>Starting a menu: just its name for the planner.</summary>
public sealed record CreateEventMenuRequest(string Title, string? DesignKey = null);

/// <summary>Everything the menu builder needs in one read.</summary>
public sealed record EventMenuWorkspaceRecord(
    IReadOnlyList<EventMenuSummaryRecord> Menus,
    IReadOnlyList<MenuCategoryRecord> Categories,
    IReadOnlyList<MenuLibraryItemRecord> Library,
    IReadOnlyList<EventMenuMealRecord> Meals,
    string EventName,
    string? VenueName,
    bool VenueIsKnownToTheSite);

/// <summary>A design a menu can be drawn in, for the picker.</summary>
public sealed record MenuDesignRecord(string Key, string Name, string Family, string Description);
