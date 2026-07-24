using System;

namespace Ben.Data.Source.Entities
{
    public partial class UploadFileRegionNote
    {
        public Guid UploadFileId { get; set; }

        /// <summary>Region start time in seconds (absolute within the audio file).</summary>
        public double RegionStart { get; set; }

        /// <summary>Region end time in seconds (absolute within the audio file).</summary>
        public double RegionEnd { get; set; }

        /// <summary>Optional human-readable label for the region.</summary>
        public string? RegionLabel { get; set; }

        /// <summary>
        /// Absolute time position in the audio file (seconds) to which this note is anchored.
        /// Null means the note applies to the whole region.
        /// </summary>
        public double? TimeOffset { get; set; }

        /// <summary>Rich-text (HTML) body produced by TelerikEditor.</summary>
        public string NoteHtml { get; set; } = null!;

        public bool IsPublic { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        // ── Navigation ────────────────────────────────────────────────────────
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
