using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What is being served on one night, and when.
    /// </summary>
    /// <remarks>
    /// <para>A menu belongs to a <see cref="HostedEventNight"/> rather than to the event, because
    /// a weekend serves a different dinner on each of its nights and a guest reading "the menu"
    /// wants the one for tonight. More than one menu per night is allowed — a supper and a
    /// midnight snack are both real — which is why the serving time lives here.</para>
    ///
    /// <para>Public to confirmed guests, not to the world: a menu is part of what somebody has
    /// booked, and a venue that has not sold the weekend yet may not want its catering costed by
    /// a competitor.</para>
    /// </remarks>
    public partial class HostedEventMenu : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventNightId { get; set; }

        /// <summary>"Dinner", "Late supper", "Breakfast".</summary>
        public string Title { get; set; } = null!;

        /// <summary>When it is served, in the event's own zone. Null when it is simply "that night".</summary>
        public TimeSpan? ServedAtLocal { get; set; }

        /// <summary>Anything true of the whole service — "in the long hall", "bring your own wine".</summary>
        public string? Notes { get; set; }

        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventNight HostedEventNight { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }

        public virtual ICollection<HostedEventMenuItem> Items { get; set; } = [];
    }

    /// <summary>One dish on a menu.</summary>
    /// <remarks>
    /// <para><b>The course is free text, not an enum.</b> Kitchens say starter, appetiser, first,
    /// pudding, dessert, sweet, sides, and "on the table"; an enum would make a venue translate its
    /// own menu into somebody else's vocabulary, and it is display-only — nothing branches on it.
    /// Ordering is by <see cref="SortOrder"/>, so a course that sorts oddly alphabetically still
    /// prints in the right place.</para>
    ///
    /// <para><b>Dietary tags describe the dish, not the guest.</b> "Vegan", "contains nuts",
    /// "gluten free" belong to the food and are shown to everybody who can see the menu.
    /// A guest's own allergy lives on <see cref="HostedEventBookingGuest.DietaryNotes"/> and is
    /// not — the two are deliberately different rows with different audiences.</para>
    /// </remarks>
    public partial class HostedEventMenuItem
    {
        public Guid Id { get; set; }
        public Guid HostedEventMenuId { get; set; }

        /// <summary>"Starter", "Main", "Pudding" — the kitchen's own word.</summary>
        public string? Course { get; set; }

        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        /// <summary>What is in it: "vegan", "contains nuts". Comma-separated, as typed.</summary>
        public string? DietaryTags { get; set; }

        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual HostedEventMenu HostedEventMenu { get; set; } = null!;
    }
}
