using System;
using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class AudioMarker
    {
        public Guid UploadFileId { get; set; }

        /// <summary>Absolute time position in the audio file (seconds) this marker is anchored to.</summary>
        public double TimeSeconds { get; set; }

        /// <summary>
        /// End of the marked span in seconds, or null for a point marker. Every marker predating
        /// span support is a point, so null stays meaningful rather than being backfilled to
        /// <see cref="TimeSeconds"/> — "this instant" and "this half-second" are different claims.
        /// </summary>
        public double? EndSeconds { get; set; }

        /// <summary>True when the detector proposed this marker rather than a person placing it.</summary>
        public bool IsAutoDetected { get; set; }

        /// <summary>
        /// The detector's 0–100 signal score, or null for a hand-placed marker. Deliberately not
        /// called a probability: it measures how far the audio stands out from its own noise floor,
        /// which is not the same as how likely it is to be a voice.
        /// </summary>
        public float? DetectionScore { get; set; }

        /// <summary>Where this marker stands in review. Defaults to Confirmed for hand-placed markers.</summary>
        public EvpReviewStatus ReviewStatus { get; set; }

        /// <summary>The clip saved from this marker, when one has been cut.</summary>
        public Guid? LinkedClipUploadFileId { get; set; }

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
        public virtual UploadFile? LinkedClipUploadFile { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
