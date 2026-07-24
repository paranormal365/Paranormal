namespace Ben.Data.Source.Entities
{
    /// <summary>Tags a timeline entry with one or more paranormal experience types.</summary>
    public class CaseTimelineEntryExperienceType
    {
        public Guid CaseTimelineEntryId { get; set; }
        public Guid ExperienceTypeId { get; set; }

        public virtual CaseTimelineEntry CaseTimelineEntry { get; set; } = null!;
        public virtual ExperienceType ExperienceType { get; set; } = null!;
    }
}
