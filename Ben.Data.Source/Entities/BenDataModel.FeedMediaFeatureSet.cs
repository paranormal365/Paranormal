namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// The measurable facts about one feed post's media, extracted once at post time
    /// (item 186 F6).
    /// </summary>
    /// <remarks>
    /// <para>These are the inputs the category-match scorer works from, kept as their own row
    /// rather than columns on <see cref="OrgMessage"/> because they describe the media, exist
    /// only for media-bearing posts, and will grow as extraction gets smarter — a sparse block
    /// of nullable columns on the busiest table in the feed would be paid for by every query
    /// that never wanted them.</para>
    ///
    /// <para>Typed columns for what is computed today; <see cref="ExtraJson"/> for what next
    /// year's extractor learns to measure, so growth is a write, not a migration. Null means
    /// "not measured", never "measured as zero" — the scorer treats the two differently.</para>
    /// </remarks>
    public class FeedMediaFeatureSet
    {
        /// <summary>One feature row per post, so the post id IS the key.</summary>
        public Guid OrgMessageId { get; set; }

        public bool IsVideo { get; set; }
        public double? DurationSeconds { get; set; }
        /// <summary>Whether an audio stream exists at all — the loudest single signal for the
        /// Audible categories.</summary>
        public bool? HasAudio { get; set; }
        public int? WidthPixels { get; set; }
        public int? HeightPixels { get; set; }
        /// <summary>Mean luminance 0–1 across the (sampled) image. Apparitions are filmed in the
        /// dark; equipment photos are not.</summary>
        public double? MeanLuma { get; set; }
        /// <summary>Luminance spread 0–1 — a flat gray frame and a high-contrast scene read very
        /// differently.</summary>
        public double? LumaStdDev { get; set; }
        /// <summary>Local hour 0–23 the media says it was captured, when it says.</summary>
        public int? CapturedHourLocal { get; set; }
        public string? CameraManufacturer { get; set; }

        // Measured by nothing yet; columns exist so the day they are measured is a backfill,
        // not a migration. See the extractor for what would fill them.
        public double? AudioAnomalyScore { get; set; }
        public int? EvpHitCount { get; set; }
        public double? MotionScore { get; set; }

        public string? ExtraJson { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
    }
}
