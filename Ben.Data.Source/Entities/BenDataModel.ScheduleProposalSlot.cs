namespace Ben.Data.Source.Entities
{
    /// <summary>A single proposed date/time option within a scheduling proposal.</summary>
    public class ScheduleProposalSlot
    {
        public Guid Id { get; set; }
        public Guid ProposalId { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public int SortOrder { get; set; }

        public virtual InvestigationScheduleProposal Proposal { get; set; } = null!;
    }
}
