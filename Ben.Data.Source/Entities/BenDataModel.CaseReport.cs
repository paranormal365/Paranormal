using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>A formal investigation report produced by the org and delivered to the client.</summary>
    public class CaseReport : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        public string Title { get; set; } = null!;

        /// <summary>HTML executive summary shown at the top of the report.</summary>
        public string? Summary { get; set; }

        /// <summary>HTML conclusion / final determination shown at the bottom.</summary>
        public string? Conclusion { get; set; }

        public CaseReportStatus Status { get; set; } = CaseReportStatus.Draft;

        /// <summary>Date communicated to the client as an expected delivery target.</summary>
        public DateTime? ExpectedDeliveryDate { get; set; }

        public DateTime? PublishedAt { get; set; }
        public Guid? PublishedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual AppUser? PublishedByAppUser { get; set; }
        public virtual ICollection<CaseReportSection> Sections { get; set; } = new List<CaseReportSection>();
    }
}
