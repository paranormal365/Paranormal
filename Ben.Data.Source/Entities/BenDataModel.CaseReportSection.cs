using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>A section within a CaseReport (narrative, evidence files, timeline, occurrences).</summary>
    public class CaseReportSection
    {
        public Guid Id { get; set; }
        public Guid CaseReportId { get; set; }
        public int SortOrder { get; set; }
        public string Title { get; set; } = null!;

        /// <summary>HTML body for Text-type sections.</summary>
        public string? Body { get; set; }

        public CaseReportSectionType SectionType { get; set; } = CaseReportSectionType.Text;

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual CaseReport CaseReport { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual ICollection<CaseReportSectionFile> Files { get; set; } = new List<CaseReportSectionFile>();

        /// <summary>Field sessions this section cites — see <see cref="CaseReportSectionFieldSession"/>.</summary>
        public virtual ICollection<CaseReportSectionFieldSession> FieldSessions { get; set; }
            = new List<CaseReportSectionFieldSession>();
    }
}
