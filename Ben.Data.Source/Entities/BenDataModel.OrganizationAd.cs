using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A group's promotional card (item 166 W3): headline, short body, optional image, and a
    /// target that may only be the group's own public page or the group finder.
    /// </summary>
    /// <remarks>
    /// The review chain (<see cref="OrganizationAdStatus"/>) is the load-bearing part: nothing
    /// unreviewed is ever served publicly. Monetization hooks (tier gating, paid placement —
    /// item 143) bolt onto the org reference later; the entity deliberately carries no money
    /// concepts today. One ACTIVE ad per group is a product rule enforced in the controller,
    /// not the schema — history rows (rejected, replaced) are worth keeping.
    /// </remarks>
    public class OrganizationAd : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string Headline { get; set; } = string.Empty;   // ≤80, enforced at the edge
        public string Body { get; set; } = string.Empty;       // ≤300, enforced at the edge
        public Guid? ImageUploadFileId { get; set; }
        /// <summary>"org" = the group's public page; "find" = the group finder. A closed set,
        /// never a free URL — a promoted card must not lead off-site.</summary>
        public string TargetKind { get; set; } = "org";
        public OrganizationAdStatus Status { get; set; } = OrganizationAdStatus.Draft;
        public string? RejectionReason { get; set; }
        public DateTime? DateSubmitted { get; set; }
        public DateTime? DateReviewed { get; set; }
        public Guid? ReviewedByAppUserId { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual UploadFile? ImageUploadFile { get; set; }
        public virtual AppUser? ReviewedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
