using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A scanner's own copy of one recording from a field session (phase 9, 10/27/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>The session is never written to.</b> A recording inside a .ben has no file row of its
    /// own, and every marker and scan hangs off one, so scanning it starts by copying its bytes,
    /// verbatim, into the scanner's files (or their group's). Markers, scans and clips then belong to
    /// the copy, and the session stays exactly as its owner sent it.</para>
    ///
    /// <para><b>One copy per recording per person, and one per group</b>, held by two filtered unique
    /// indexes. Opening the recording again finds the same copy, with the markers left on it.</para>
    ///
    /// <para><b>Only the copy has a foreign key.</b> Deleting the copy removes this row. The session
    /// columns have none: a session can be deleted by its owner, by its investigation going, or by a
    /// purge, and the copy is the scanner's file, not the session's, so it stays.</para>
    /// </remarks>
    public partial class FieldSessionAnalysisCopy
    {
        public Guid Id { get; set; }

        /// <summary>The session the recording came from. No foreign key (see remarks).</summary>
        public Guid FieldSessionUploadId { get; set; }

        /// <summary>The recording within it. No foreign key.</summary>
        public Guid FieldSessionUploadFileId { get; set; }

        /// <summary>The copy: an ordinary audio file the editor opens.</summary>
        public Guid CopyUploadFileId { get; set; }

        public AnalysisScope Scope { get; set; }

        /// <summary>Set for a person's copy, and only then.</summary>
        public Guid? ScopeAppUserId { get; set; }

        /// <summary>Set for a group's copy, and only then.</summary>
        public Guid? ScopeOrganizationId { get; set; }

        /// <summary>The digest the copied bytes were checked against.</summary>
        public string? SourceSha256 { get; set; }

        /// <summary>Where the recording sits in the session, e.g. <c>media/audio-002.m4a</c>.</summary>
        public string SourceRelativePath { get; set; } = string.Empty;

        public Guid MadeByAppUserId { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual UploadFile CopyUploadFile { get; set; } = null!;
    }
}
