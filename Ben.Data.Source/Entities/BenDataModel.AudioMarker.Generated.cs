using System;
using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class AudioMarker
    {
        public Guid UploadFileId { get; set; }

        /// <summary>Absolute time position in the audio file (seconds) this marker is anchored to.</summary>
        public double TimeSeconds { get; set; }

        /// <summary>Short human-readable label for the marker (e.g. "Whispered name?").</summary>
        public string Label { get; set; } = null!;

        /// <summary>Investigator's confidence rating for this EVP.</summary>
        public EvpConfidenceLevel ConfidenceLevel { get; set; }

        /// <summary>Optional free-form note elaborating on the marker.</summary>
        public string? Note { get; set; }

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
