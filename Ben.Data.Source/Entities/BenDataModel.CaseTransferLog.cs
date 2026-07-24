using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Records a proposal to transfer a case from one organization to another.
    /// The receiving org must accept; on acceptance the Case.OrganizationId is updated.
    /// </summary>
    public class CaseTransferLog
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid FromOrganizationId { get; set; }
        public Guid ToOrganizationId { get; set; }
        public Guid ProposedByAppUserId { get; set; }
        public Guid? RespondedByAppUserId { get; set; }
        public CaseTransferStatus Status { get; set; } = CaseTransferStatus.Pending;
        public string? TransferReason { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime DateProposed { get; set; }
        public DateTime? DateResponded { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual Organization FromOrganization { get; set; } = null!;
        public virtual Organization ToOrganization { get; set; } = null!;
        public virtual AppUser ProposedByAppUser { get; set; } = null!;
        public virtual AppUser? RespondedByAppUser { get; set; }
    }
}
