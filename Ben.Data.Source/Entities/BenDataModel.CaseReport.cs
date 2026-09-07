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

        /// <summary>
        /// The group has chosen to show this report's summary and conclusion on the public case
        /// page (site evaluation 2026-09-06, W-P3). Off unless somebody says otherwise.
        /// </summary>
        /// <remarks>
        /// <para><b>Three conditions, and this is only one of them.</b> It means nothing until the
        /// report is <see cref="CaseReportStatus.Published"/> and the case itself is public.
        /// Publishing a report delivers it to the client, which is not a decision about the world;
        /// making a case public releases the case, which says nothing about a document written for
        /// its owner. Neither implies this, so it is asked separately.</para>
        ///
        /// <para>Only the summary and the conclusion are ever released — never the sections, the
        /// evidence files or the field sessions, which carry the working detail of an investigation
        /// in somebody's home.</para>
        /// </remarks>
        public bool IsPublicSummaryVisible { get; set; }

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
