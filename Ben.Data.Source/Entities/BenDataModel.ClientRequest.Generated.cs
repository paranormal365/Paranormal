using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A registered user's request for an organization to investigate their location.
    /// Progresses from Draft → Submitted → Assigned once an org accepts.
    /// </summary>
    public partial class ClientRequest
    {
        /// <summary>The user who submitted this request (the "client").</summary>
        public Guid AppUserId { get; set; }

        public ClientRequestStatus Status { get; set; } = ClientRequestStatus.Draft;

        // ── Location ──────────────────────────────────────────────────────────
        public string StreetAddress1 { get; set; } = null!;
        public string? StreetAddress2 { get; set; }
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string ZipCode { get; set; } = null!;
        public string Country { get; set; } = "US";

        /// <summary>Geocoded latitude — used for proximity matching. Not exposed publicly.</summary>
        public decimal? Latitude { get; set; }

        /// <summary>Geocoded longitude — used for proximity matching. Not exposed publicly.</summary>
        public decimal? Longitude { get; set; }

        // ── Client details ────────────────────────────────────────────────────
        public ClientGender Gender { get; set; } = ClientGender.NotProvided;

        /// <summary>Optional birth year. Null means the client chose not to provide it.</summary>
        public int? BirthYear { get; set; }

        // ── Narrative ─────────────────────────────────────────────────────────
        /// <summary>HTML description of what the client has experienced at the location.</summary>
        public string? Description { get; set; }

        // ── Audit ─────────────────────────────────────────────────────────────
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<ClientRequestOrganization> OrganizationApplications { get; set; } = new List<ClientRequestOrganization>();
        public virtual ICollection<ClientRequestFile> Files { get; set; } = new List<ClientRequestFile>();
    }
}
