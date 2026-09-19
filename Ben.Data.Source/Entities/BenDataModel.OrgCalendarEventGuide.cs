namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Who is leading one particular date (item 233).
    /// </summary>
    /// <remarks>
    /// <para>Seeded from the tour's guides when a date is created, and changeable afterwards,
    /// because <b>Ben, 2026-09-10</b>: "if it is led by more than one person, it would not be the
    /// same picture for each tour." The guest mail names these people and shows their photograph
    /// where they have made one public — Ben's reason was safety, so the picture must be of
    /// whoever will actually be standing there on that night, not of the tour in general.</para>
    /// </remarks>
    public partial class OrgCalendarEventGuide
    {
        public Guid Id { get; set; }
        public Guid OrgCalendarEventId { get; set; }
        public Guid AppUserId { get; set; }
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
