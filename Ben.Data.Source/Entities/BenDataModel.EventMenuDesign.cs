using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A heading a menu's items sit under: Appetizers, Sandwiches, Drinks (Ben, 10/13/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>One list for the whole site.</b> Ben: "All the possible categories will be available.
    /// Also, the event planner will be able to create one if it is missing. Only unique values would
    /// be accepted and they would be added to any event in the future." So a category a planner adds
    /// is offered to every planner after them, and a second "Desserts" is refused rather than made.</para>
    ///
    /// <para><see cref="NameKey"/> is what uniqueness is checked on: the name lower-cased with its
    /// spaces and punctuation taken out, so "Soft drinks", "Soft Drinks" and "soft-drinks" are one.</para>
    /// </remarks>
    public partial class MenuCategory
    {
        public Guid Id { get; set; }

        /// <summary>As the planner typed it, and as the menu prints it.</summary>
        public string Name { get; set; } = null!;

        /// <summary>The name reduced for comparison; unique.</summary>
        public string NameKey { get; set; } = null!;

        /// <summary>Where a new section of this category goes on a menu, before the planner moves it.</summary>
        public int SortOrder { get; set; }

        /// <summary>Whether the site started with it, rather than a planner adding it.</summary>
        public bool IsStarter { get; set; }

        public DateTime DateCreated { get; set; }

        /// <summary>Who added it; null for the starters.</summary>
        public Guid? CreatedByAppUserId { get; set; }

        /// <summary>Reduces a name to the form uniqueness is checked on.</summary>
        public static string KeyFor(string name)
            => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    /// <summary>
    /// One thing a menu can offer: a dish, a drink, a bag of candy (Ben, 10/13/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>Made once, put on as many menus as want it.</b> A menu entry points here, so fixing a
    /// description fixes it on every menu that offers the dish.</para>
    ///
    /// <para><b>Where it is kept</b> is <see cref="Scope"/>: this event, the group (every event it
    /// holds), or the venue (every event at <see cref="PlaceId"/>, whichever group holds it). The
    /// group that made it always owns it; a venue item can also be changed by the place's verified
    /// venue group.</para>
    ///
    /// <para><b>Retired, not deleted.</b> A menu that already offers it keeps drawing it; it simply
    /// stops being offered for new menus.</para>
    /// </remarks>
    public partial class MenuLibraryItem : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The group that made it, and owns it.</summary>
        public Guid OrganizationId { get; set; }

        public MenuItemScope Scope { get; set; }

        /// <summary>The event, when <see cref="Scope"/> is Event.</summary>
        public Guid? HostedEventId { get; set; }

        /// <summary>The venue, when <see cref="Scope"/> is Venue.</summary>
        public Guid? PlaceId { get; set; }

        public Guid MenuCategoryId { get; set; }

        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        /// <summary>The item's picture, fitted and stripped of its location like a gallery photo.</summary>
        public Guid? ImageUploadFileId { get; set; }

        public MenuDietary Dietary { get; set; }
        public MenuAllergen Allergens { get; set; }

        /// <summary>Anything else worth saying about what's in it: "made in a kitchen that uses nuts".</summary>
        public string? DietaryNote { get; set; }

        public DateTime? RetiredUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual HostedEvent? HostedEvent { get; set; }
        public virtual Place? Place { get; set; }
        public virtual MenuCategory MenuCategory { get; set; } = null!;
        public virtual UploadFile? ImageUploadFile { get; set; }
    }

    /// <summary>
    /// A designed menu for an event: sections of items, prices, a look, and the meals it is served at
    /// (Ben, 10/13/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>Not seen until it is published.</b> <see cref="Title"/> is the planner's own name for
    /// it ("Friday dinner, adults"); guests read <see cref="Heading"/>.</para>
    ///
    /// <para><b>Where it is served</b> is either the meals it is attached to (<see cref="Servings"/>),
    /// or the whole event (<see cref="ThroughoutTheEvent"/>), which is for things on offer the whole
    /// time: candy, bottled water and soft drinks, sold or free with the event (Ben, 10/13/2026).</para>
    ///
    /// <para>The meal (<see cref="Meal"/>) is null until the planner chooses one.</para>
    /// </remarks>
    public partial class EventMenu : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>The planner's name for it. Guests never see it.</summary>
        public string Title { get; set; } = null!;

        /// <summary>What guests see at the top: "Dinner", "The Midnight Supper". Falls back to the meal.</summary>
        public string? Heading { get; set; }

        /// <summary>A line or two under the heading.</summary>
        public string? Intro { get; set; }

        public MenuMeal? Meal { get; set; }

        /// <summary>On offer for the whole event rather than at its meals.</summary>
        public bool ThroughoutTheEvent { get; set; }

        /// <summary>Which look it is drawn in; a key into the site's designs.</summary>
        public string DesignKey { get; set; } = "classic-bistro";

        /// <summary>A logo or picture drawn at the top.</summary>
        public Guid? LogoUploadFileId { get; set; }

        /// <summary>A line under the prices, such as "Gratuity not included".</summary>
        public string? PriceNote { get; set; }

        public DateTime? PublishedUtc { get; set; }

        /// <summary>The menu this was copied from, for its "Copy N" name.</summary>
        public Guid? CopiedFromEventMenuId { get; set; }

        /// <summary>The meal whose typed dishes this was made from, when it was converted from the old
        /// menus (10/13/2026); what keeps the conversion from running twice.</summary>
        public Guid? ConvertedFromHostedEventMenuId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public bool IsPublished => PublishedUtc is not null;

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual UploadFile? LogoUploadFile { get; set; }
        public virtual ICollection<EventMenuPrice> Prices { get; set; } = [];
        public virtual ICollection<EventMenuSection> Sections { get; set; } = [];
        public virtual ICollection<EventMenuServing> Servings { get; set; } = [];
    }

    /// <summary>One meal price on a menu: "Adults $25", "Children under 12 $12".</summary>
    public partial class EventMenuPrice
    {
        public Guid Id { get; set; }
        public Guid EventMenuId { get; set; }

        /// <summary>Who it is for. Empty when the menu has one price for everybody.</summary>
        public string? Label { get; set; }

        public decimal Amount { get; set; }
        public int SortOrder { get; set; }

        public virtual EventMenu EventMenu { get; set; } = null!;
    }

    /// <summary>One category as a section of a menu, in the planner's order.</summary>
    public partial class EventMenuSection
    {
        public Guid Id { get; set; }
        public Guid EventMenuId { get; set; }
        public Guid MenuCategoryId { get; set; }

        /// <summary>A heading for this menu only, in place of the category's name: "From the grill".</summary>
        public string? Heading { get; set; }

        public int SortOrder { get; set; }

        public virtual EventMenu EventMenu { get; set; } = null!;
        public virtual MenuCategory MenuCategory { get; set; } = null!;
        public virtual ICollection<EventMenuEntry> Entries { get; set; } = [];
    }

    /// <summary>One item on a menu, and how it is paid for.</summary>
    public partial class EventMenuEntry
    {
        public Guid Id { get; set; }
        public Guid EventMenuSectionId { get; set; }
        public Guid MenuLibraryItemId { get; set; }

        public MenuPricing Pricing { get; set; } = MenuPricing.IncludedInMeal;

        /// <summary>Required when <see cref="Pricing"/> is PricedSeparately; otherwise null.</summary>
        public decimal? Price { get; set; }

        /// <summary>A line for this menu only: "add blue cheese", "while it lasts".</summary>
        public string? Note { get; set; }

        public int SortOrder { get; set; }

        public virtual EventMenuSection EventMenuSection { get; set; } = null!;
        public virtual MenuLibraryItem MenuLibraryItem { get; set; } = null!;
    }

    /// <summary>A menu served at one of the event's meals. A meal can show several menus.</summary>
    /// <remarks>
    /// The meal is a <see cref="HostedEventMenu"/> row: one sitting of one night, which is what the
    /// dining tables are laid out against, so attaching menus to it leaves the seating alone.
    /// </remarks>
    public partial class EventMenuServing
    {
        public Guid EventMenuId { get; set; }
        public Guid HostedEventMenuId { get; set; }

        public virtual EventMenu EventMenu { get; set; } = null!;
        public virtual HostedEventMenu Meal { get; set; } = null!;
    }
}
