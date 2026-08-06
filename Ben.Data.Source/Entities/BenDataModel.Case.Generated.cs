using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A paranormal investigation case owned by an organization.
    /// Originates either from an accepted <see cref="ClientRequest"/> or from
    /// a member-proposed internal investigation.
    /// </summary>
    public partial class Case
    {
        public Guid OrganizationId { get; set; }

        /// <summary>Source client request; null when internally proposed by a member.</summary>
        public Guid? ClientRequestId { get; set; }

        /// <summary>The org member assigned as the primary case manager.</summary>
        public Guid? CaseManagerAppUserId { get; set; }

        public CaseStatus Status { get; set; } = CaseStatus.Proposed;

        /// <summary>Brief title identifying the case (e.g. "Smith, Nashville TN").</summary>
        public string Title { get; set; } = null!;

        /// <summary>
        /// The calendar year in which this case was opened.
        /// Combined with <see cref="OrgCaseNumber"/> forms the human reference: #2026-042.
        /// </summary>
        public int CaseYear { get; set; }

        /// <summary>
        /// Sequential case number within this organization for <see cref="CaseYear"/>.
        /// Assigned automatically on creation. Combined with CaseYear: #2026-042.
        /// </summary>
        public int OrgCaseNumber { get; set; }

        /// <summary>HTML narrative summarizing the case and the reported activity.</summary>
        public string? Description { get; set; }

        // ── Investigation address ─────────────────────────────────────────────
        // Copied from ClientRequest or entered manually; stored on the Case so it
        // remains accurate even if the original request is later modified.
        public string StreetAddress1 { get; set; } = null!;
        public string? StreetAddress2 { get; set; }
        public string City { get; set; } = null!;
        public string State { get; set; } = null!;
        public string ZipCode { get; set; } = null!;
        public string Country { get; set; } = "US";
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        // ── Visibility ────────────────────────────────────────────────────────
        /// <summary>
        /// Pseudonym shown on public pages instead of the client's real name.
        /// Null = use real name (only applicable when IsPublic = true).
        /// </summary>
        public string? PublicPseudonym { get; set; }

        /// <summary>When true, the case and its public pages are visible to everyone.</summary>
        public bool IsPublic { get; set; }

        public DateTime DateCaseOpened { get; set; }
        public DateTime? DateCaseClosed { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public virtual Organization Organization { get; set; } = null!;
        public virtual ClientRequest? ClientRequest { get; set; }
        public virtual AppUser? CaseManagerAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<CaseTimelineEntry> TimelineEntries { get; set; } = new List<CaseTimelineEntry>();
        public virtual ICollection<CaseFile> CaseFiles { get; set; } = new List<CaseFile>();
        public virtual ICollection<CaseRelatedPerson> RelatedPeople { get; set; } = new List<CaseRelatedPerson>();
    }
}
