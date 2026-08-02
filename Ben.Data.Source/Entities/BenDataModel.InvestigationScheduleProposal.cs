using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>An org's request to the client to agree on an investigation date.</summary>
    public class InvestigationScheduleProposal : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        public ScheduleProposalStatus Status { get; set; } = ScheduleProposalStatus.Pending;

        /// <summary>Message to the client explaining the proposed dates.</summary>
        public string? Notes { get; set; }

        // ── Client response ───────────────────────────────────────────────────
        /// <summary>The slot the client accepted (null until accepted).</summary>
        public Guid? AcceptedSlotId { get; set; }

        /// <summary>Alternative date/time proposed by the client (Countered status).</summary>
        public DateTime? ClientCounterDateTime { get; set; }

        public string? ClientResponseNotes { get; set; }
        public DateTime? ClientRespondedAt { get; set; }

        /// <summary>The Investigation created when the client accepts (or org converts).</summary>
        public Guid? InvestigationId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual Investigation? Investigation { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<ScheduleProposalSlot> Slots { get; set; } = new List<ScheduleProposalSlot>();
    }
}
