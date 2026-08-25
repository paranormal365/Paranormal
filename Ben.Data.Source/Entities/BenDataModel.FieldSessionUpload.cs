using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A field session an investigator recorded on their phone and later sent up.
    /// </summary>
    /// <remarks>
    /// <para><b>The session runs offline.</b> Somebody records for five hours in a building with
    /// no signal, drives home, and only then has the bandwidth to send anything. So this row is
    /// created when the DOCUMENT arrives, and the recordings follow one at a time afterwards —
    /// see <see cref="FieldSessionUploadFile"/>. One dropped connection costs one file, not the
    /// night.</para>
    ///
    /// <para><b>The upload IS the record</b>, for the reason
    /// <see cref="EventEvidenceSubmission"/> already records: the case timeline is case-scoped,
    /// and an investigation often has no case, so converting into a timeline entry would lose
    /// exactly the sessions that have nowhere else to live. When the investigation DOES have a
    /// case, one <c>InstrumentReading</c> entry is written alongside so the session shows up in
    /// the case binder — one per session, never one per reading, because a five-hour interval
    /// log would bury the timeline it was meant to inform.</para>
    ///
    /// <para><b>The document is the evidence.</b> It arrives as a Device Data Format v1
    /// <c>data.json</c> (ProjectNotes/specs/DeviceDataFormat-v1.md), stored verbatim as an upload
    /// file rather than shredded into columns. The counts here are for listing sessions without
    /// opening it; anything more detailed is read from the document itself, which is the only
    /// copy that is definitely what the device wrote.</para>
    /// </remarks>
    public class FieldSessionUpload : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>
        /// The investigation this was recorded for, when there is one.
        /// </summary>
        /// <remarks>
        /// Null for a personal session — somebody scouting a building, a tour guide walking a
        /// route, or a member recording something that is nobody's case yet. Those belong to the
        /// ACCOUNT that sent them (<see cref="SubmittedByAppUserId"/>) and are visible only to
        /// them. An investigation can be chosen later, when there is one to choose.
        /// </remarks>
        public Guid? InvestigationId { get; set; }

        /// <summary>Who sent it up. Not necessarily who recorded it — somebody may hand a device
        /// to a colleague to upload.</summary>
        public Guid SubmittedByAppUserId { get; set; }

        /// <summary>
        /// Who was signed in on the device when the session was RECORDED.
        /// </summary>
        /// <remarks>
        /// Null when nobody was — the app records without an account so a member standing in a
        /// cellar with no signal is never stopped, and being signed in needs no connectivity
        /// anyway. An unattributed session is still evidence; it is simply evidence nobody has
        /// put their name to, and it says so rather than quietly borrowing the uploader's.
        /// </remarks>
        public Guid? RecordedByAppUserId { get; set; }

        /// <summary>The name shown on the device at the time, kept as written. A display name
        /// can change afterwards, and the record should say who this was THEN.</summary>
        public string? RecordedByName { get; set; }

        /// <summary>
        /// The device's own id for the session. Carried so a second submission of the same
        /// session updates rather than duplicates — a flaky upload retried is common, and two
        /// copies of one night is worse than none.
        /// </summary>
        public Guid DeviceSessionId { get; set; }

        /// <summary>The stored <c>data.json</c>, exactly as the device wrote it.</summary>
        public Guid DocumentUploadFileId { get; set; }

        /// <summary>Hardware identifier — "iPhone17,1". A reading cannot be assessed for known
        /// quirks without knowing what took it.</summary>
        public string DeviceModel { get; set; } = null!;

        /// <summary>The operator's own words for where this was — "back bedroom, north wall".</summary>
        public string? LocationLabel { get; set; }

        public DateTime StartedAt { get; set; }

        /// <summary>Null when the session was interrupted — the phone died, the app was killed.
        /// The honest answer to when it stopped is that nobody knows.</summary>
        public DateTime? EndedAt { get; set; }

        public int ReadingCount { get; set; }
        public int MarkerCount { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Investigation? Investigation { get; set; }
        public virtual AppUser SubmittedByAppUser { get; set; } = null!;
        public virtual AppUser? RecordedByAppUser { get; set; }
        public virtual UploadFile DocumentUploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<FieldSessionUploadFile> Files { get; set; }
            = new List<FieldSessionUploadFile>();
    }

    /// <summary>
    /// One recording belonging to an uploaded field session.
    /// </summary>
    /// <remarks>
    /// <para><see cref="RelativePath"/> is the name the document refers to this file by —
    /// <c>media/audio-001.m4a</c>. That is what ties a reading's <c>audio_ref</c> to actual
    /// bytes, so it is stored rather than derived from the upload's own file name.</para>
    ///
    /// <para><see cref="Sha256"/> is the digest the DEVICE computed, checked against the bytes on
    /// arrival. Audio attached to the wrong reading is worse than no audio, and a truncated
    /// upload that nobody noticed is worse still.</para>
    /// </remarks>
    public class FieldSessionUploadFile : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid FieldSessionUploadId { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>The path the document names this file by. Relative, always.</summary>
        public string RelativePath { get; set; } = null!;

        /// <summary>Lowercase hex, 64 characters, as computed on the device.</summary>
        public string? Sha256 { get; set; }

        /// <summary>False when the bytes that arrived did not match the digest that came with
        /// them. Kept rather than rejected, so somebody can see what happened.</summary>
        public bool DigestMatched { get; set; } = true;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual FieldSessionUpload FieldSessionUpload { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
