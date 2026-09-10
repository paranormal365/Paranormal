namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Somebody the business has put on a tour as a guide (item 233).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-10:</b> "The tour owner is not necessarily the tour guide. So they
    /// would need to be able to add guides to the tour or guide." So this is a list the owner
    /// keeps, not a fact derived from who runs the business — an owner who never walks a street
    /// is on no tour, and a guide who walks every night has no say over settings.</para>
    ///
    /// <para>A guide must be an active member of the business, because a guide's name and face
    /// go out to guests as the person they will meet in the dark, and that promise must point at
    /// somebody the business has actually taken on.</para>
    /// </remarks>
    public partial class TourGuide
    {
        public Guid Id { get; set; }
        public Guid TourId { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>The order they are listed in, which is also the order a date inherits.</summary>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual Tour Tour { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
