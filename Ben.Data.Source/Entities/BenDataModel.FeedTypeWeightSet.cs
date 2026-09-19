namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One fitted set of category-match weights for one experience type (item 186 F6).
    /// Versioned and append-only: a re-fit writes a NEW row, never edits the old one.
    /// </summary>
    /// <remarks>
    /// <para>The active set for a type is simply its highest <see cref="FitVersion"/>. Keeping
    /// every version makes a bad fit a one-row revert and every scoring decision auditable —
    /// "why was this post nudged" has an answer months later, which matters the day a poster
    /// disputes one.</para>
    ///
    /// <para><see cref="HoldoutAccuracy"/> is the fit's honesty check: measured on the 20% of
    /// examples the fit never saw. A re-fit that scores worse than the current active set on
    /// holdout is still recorded (the row is cheap) but the job logs it loudly; nothing
    /// auto-reverts, because thirty examples of noise should not silently unseat a good fit.</para>
    /// </remarks>
    public class FeedTypeWeightSet
    {
        public Guid Id { get; set; }
        public Guid ExperienceTypeId { get; set; }

        /// <summary>Monotonic per type. Version 0 is the hand-written prior, present from seed
        /// so scoring works before the first example exists.</summary>
        public int FitVersion { get; set; }

        public DateTime FitUtc { get; set; }

        /// <summary>How many labelled examples the fit saw. Zero for the version-0 priors.</summary>
        public int ExampleCount { get; set; }

        /// <summary>Feature name → weight, plus the reserved key <c>_bias</c>.</summary>
        public string WeightsJson { get; set; } = null!;

        /// <summary>Accuracy on the held-out 20%; null when there was too little to hold out.</summary>
        public double? HoldoutAccuracy { get; set; }

        public virtual ExperienceType ExperienceType { get; set; } = null!;
    }
}
