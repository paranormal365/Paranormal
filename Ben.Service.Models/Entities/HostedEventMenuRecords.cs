namespace Ben.Service.Models.Entities;

/// <summary>One dish on a menu, as both the kitchen's screen and a guest's read it.</summary>
/// <param name="Course">
/// The kitchen's own word — "Starter", "Pudding", "On the table". Display only; nothing branches
/// on it, which is why it is not an enum.
/// </param>
/// <param name="DietaryTags">
/// What is in the DISH: "vegan", "contains nuts". Shown to everybody who can see the menu. A
/// guest's own allergy is a different thing with a different audience and never appears here.
/// </param>
public sealed record HostedEventMenuItemRecord(
    Guid Id,
    string? Course,
    string Name,
    string? Description,
    string? DietaryTags,
    int SortOrder);

/// <summary>
/// What is served at one sitting, and when.
/// </summary>
/// <remarks>
/// A night has as many of these as the venue serves — breakfast, lunch, dinner, a late supper,
/// snacks on the table. They are read in <see cref="SortOrder"/>, which is the order the host
/// arranged them, because a night runs from the evening through the next morning and sorting on
/// the clock would print breakfast before the dinner it followed.
/// </remarks>
/// <param name="ServedAtLocal">
/// In the event's own zone, because a guest reading "8pm" means the clock on the wall where the
/// dinner is. Null when it is simply "that night".
/// </param>
public sealed record HostedEventMenuRecord(
    Guid Id,
    Guid HostedEventNightId,
    DateTime NightDate,
    string? NightTitle,
    string Title,
    TimeSpan? ServedAtLocal,
    string? Notes,
    int SortOrder,
    IReadOnlyList<HostedEventMenuItemRecord> Items);

/// <summary>Every sitting of the whole event, in the order they are served.</summary>
public sealed record HostedEventMenusRecord(
    Guid HostedEventId,
    IReadOnlyList<HostedEventMenuRecord> Menus);

/// <summary>
/// The whole set of menus for an event, replacing whatever was there.
/// </summary>
/// <remarks>
/// Replace-the-set rather than per-menu editing, for the same reason the offered rooms are: the
/// screen is one card the host fills in and saves, and a half-saved menu with the pudding missing
/// is worse than no menu at all.
/// </remarks>
/// <param name="Menus">
/// Every sitting of the whole event, in the order they happen. <b>Position in this list is the
/// order</b> — for the sittings and for the dishes inside them — so a screen that lets a host drag
/// a row simply sends the list as it now reads.
/// </param>
public sealed record SetHostedEventMenusRequest(IReadOnlyList<HostedEventMenuInput> Menus);

/// <summary>One sitting to write.</summary>
/// <param name="Title">"Breakfast", "Lunch", "Dinner", "Snacks" — whatever the venue calls it.</param>
public sealed record HostedEventMenuInput(
    Guid HostedEventNightId,
    string Title,
    TimeSpan? ServedAtLocal = null,
    string? Notes = null,
    IReadOnlyList<HostedEventMenuItemInput>? Items = null);

public sealed record HostedEventMenuItemInput(
    string Name,
    string? Course = null,
    string? Description = null,
    string? DietaryTags = null);
