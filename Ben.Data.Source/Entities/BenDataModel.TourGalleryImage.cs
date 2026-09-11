using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A picture on a tour's public page (item 233).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-10:</b> "The tour can keep up to 50 1920x1080 72ppi images and they
    /// can swap them out or tag ones from tours to keep as well, but 50 images per tour max."
    /// So the gallery is both the tour's shop window and the <b>keep</b> for photographs: a shot a
    /// guest sent in has a clock on it, and putting it here is what stops the clock.</para>
    ///
    /// <para>The row points at its own <see cref="UploadFile"/> — a copy resized to fit inside
    /// 1920×1080 — rather than at the guest's original. The original keeps its own expiry and its
    /// own owner; the business keeps a picture it may publish. Nobody's photograph is quietly
    /// taken over, and nobody's page breaks when an original goes.</para>
    /// </remarks>
    public partial class TourGalleryImage : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid TourId { get; set; }

        /// <summary>The business's own copy, sized for a web page.</summary>
        public Guid UploadFileId { get; set; }

        /// <summary>Where it sits in the gallery; the first one is the tour's picture.</summary>
        public int SortOrder { get; set; }

        /// <summary>What it shows. Doubles as the alt text, so it is never decoration.</summary>
        public string? Caption { get; set; }

        /// <summary>
        /// The guest submission this was kept from, when it came from a night out rather than
        /// from the business's own camera.
        /// </summary>
        public Guid? SourceEventEvidenceSubmissionId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Tour Tour { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }

        /// <summary>How many pictures one tour may keep (Ben, 2026-09-10).</summary>
        public const int MaxPerTour = 50;

        /// <summary>The box a kept picture is fitted inside, in pixels.</summary>
        /// <remarks>
        /// Fitted, never stretched: a portrait photograph comes out 1080 tall and narrower, which
        /// is what "1920x1080" means for a picture that is not already that shape. Resolution in
        /// dots per inch is a printing idea with no meaning for a picture on a screen, so nothing
        /// here writes one.
        /// </remarks>
        public const int MaxWidth = 1920;
        public const int MaxHeight = 1080;
    }
}
