using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// How one file was made from a field session's recording, step by step — the certificate of
    /// authenticity behind an enhanced clip (10/28/2026).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 10/28/2026: the session's .ben is never edited. Its owner, or an organizer holding a
    /// copy, runs the audio through our analyzer, marks the EVPs and may enhance the sound; an enhanced
    /// recording is another clip, attached to the session from outside, keeping its time in the session
    /// and saying it was enhanced — with proof that it came from the original and was changed only by
    /// our own processing, not generated and not edited elsewhere.</para>
    ///
    /// <para><b>What it proves.</b> The chain starts at the scanner's copy of the recording, whose bytes
    /// were hashed as they were copied out of the .ben and checked against the hash the phone sealed
    /// (<see cref="SealSha256"/>, when the seal could be read). Each step after it is one of our own
    /// operations with its settings, and <see cref="ResultSha256"/> is this file's bytes as written.
    /// <see cref="Signature"/> is the site's seal over all of it, so a row edited in the database no
    /// longer verifies. Replacing a certified file's bytes is refused (<c>UploadFileController.Replace</c>).</para>
    ///
    /// <para><b>Only the file has a foreign key.</b> Deleting the file removes its certificate; the
    /// session and the copy are recorded by id only, so either can go and the history stays readable.</para>
    /// </remarks>
    public partial class FileProvenance
    {
        public Guid Id { get; set; }

        /// <summary>The file this certifies.</summary>
        public Guid UploadFileId { get; set; }

        /// <summary>The session whose recording it came from. No foreign key.</summary>
        public Guid FieldSessionUploadId { get; set; }

        /// <summary>The scanner's copy the chain starts from. No foreign key.</summary>
        public Guid AnalysisCopyUploadFileId { get; set; }

        /// <summary>Whose copy that was: a person's, or a group's (decides who sees the file publicly).</summary>
        public AnalysisScope Scope { get; set; }
        public Guid? ScopeAppUserId { get; set; }
        public Guid? ScopeOrganizationId { get; set; }

        /// <summary>Where the recording sits in the session, e.g. <c>media/audio-001.m4a</c>.</summary>
        public string SourceClipPath { get; set; } = string.Empty;

        /// <summary>What the phone sealed for that recording, when the session's seal could be read.</summary>
        public string? SealSha256 { get; set; }

        /// <summary>The copy's bytes as they were copied out of the session.</summary>
        public string CopySha256 { get; set; } = string.Empty;

        /// <summary>The steps from the copy to this file, oldest first, as JSON (<c>ProvenanceStep[]</c>).</summary>
        public string ChainJson { get; set; } = "[]";

        /// <summary>This file's bytes as they were written.</summary>
        public string ResultSha256 { get; set; } = string.Empty;

        /// <summary>
        /// Where this file starts in the recording, in ms; null once a step that moves time (a cut,
        /// a speed or pitch change, reversing) has been applied, because it then has no one place.
        /// </summary>
        public long? RecordingOffsetMs { get; set; }

        public long? DurationMs { get; set; }

        /// <summary>
        /// Only steps that keep the sound and its timing: copy, clip, normalize, gain, fade. Shown in the
        /// session's place on its timeline. Anything else is certified too, but as altered.
        /// </summary>
        public bool Enhanced { get; set; }

        public Guid MadeByAppUserId { get; set; }
        public DateTime IssuedUtc { get; set; }

        /// <summary>The site's seal over the row (<c>ProvenanceSigner</c>, the API's data-protection keys).</summary>
        public string? Signature { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
    }
}
