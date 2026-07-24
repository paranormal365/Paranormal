using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Records that a client applied to a specific organization with their request.
    /// A client may apply to a maximum of 2 organizations; the first to accept gets the case.
    /// </summary>
    public partial class ClientRequestOrganization
    {
        public Guid ClientRequestId { get; set; }
        public Guid OrganizationId { get; set; }
        public ClientOrgRequestStatus Status { get; set; } = ClientOrgRequestStatus.Pending;
        public DateTime DateApplied { get; set; }
        public DateTime? DateResponded { get; set; }

        /// <summary>The org member who accepted or rejected this application.</summary>
        public Guid? RespondedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual ClientRequest ClientRequest { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser? RespondedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
