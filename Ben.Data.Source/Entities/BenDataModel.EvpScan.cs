using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One EVP scan somebody ran, with the settings it ran at (item 253, step one).
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is kept.</b> Reviewers' Keep and Dismiss rulings are the labels the detector
    /// will one day learn from, and a label is only useful next to what produced it. This row says
    /// which detector version and which settings found the candidates in
    /// <see cref="Candidates"/>. It also turns a hand-placed marker into evidence: a marker on a
    /// recording with a scan before it, where no candidate overlapped, is something the detector
    /// missed. Without the scan record a miss looks the same as a recording nobody scanned.</para>
    ///
    /// <para><b>Write-once, and never in the way.</b> Nothing reads these rows while anybody uses
    /// the editor, and failing to write one never fails the scan. They hold measurements and
    /// times, never audio and never a person: who scanned is not needed to learn from it.</para>
    ///
    /// <para>Deleting the recording deletes its scans. A person's file leaves with everything that
    /// was measured from it.</para>
    /// </remarks>
    public partial class EvpScan
    {
        public Guid Id { get; set; }

        public Guid UploadFileId { get; set; }

        public DateTime DateCreated { get; set; }

        /// <summary><c>EvpDetector.Version</c> at the time: which scoring made the candidates.</summary>
        public int DetectorVersion { get; set; }

        /// <summary>The preset the person picked. The values below are what actually ran.</summary>
        public EvpSensitivity Sensitivity { get; set; }

        public double ThresholdDb { get; set; }
        public double MinDurationSeconds { get; set; }
        public double MergeGapSeconds { get; set; }
        public double ContextPadSeconds { get; set; }
        public double MaxEventSeconds { get; set; }

        /// <summary>Length of the recording that was scanned.</summary>
        public double RecordingSeconds { get; set; }

        /// <summary>How many the detector found (after its cap of the highest-scoring).</summary>
        public int FoundCount { get; set; }

        /// <summary>How many of those reached the review queue; the rest overlapped something already ruled on.</summary>
        public int ProposedCount { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;

        public virtual ICollection<EvpScanCandidate> Candidates { get; set; } = new List<EvpScanCandidate>();
    }

    /// <summary>
    /// One thing a scan found, with what the detector measured about it (item 253).
    /// </summary>
    /// <remarks>
    /// <para><b>Not the marker.</b> The marker is the reviewer's working copy: it gets relabeled,
    /// re-bounded, replaced by the next scan or deleted. This row is what the detector said at the
    /// time, and it does not change. The label is read from the marker
    /// (<see cref="AudioMarkerId"/>) and from <see cref="EvpRuling"/> when anybody wants to learn
    /// from it.</para>
    ///
    /// <para><b><see cref="AudioMarkerId"/> has no foreign key on purpose.</b> A re-scan deletes
    /// the Pending markers it replaces, and those rows should outlive that: "proposed, never ruled
    /// on" is itself worth knowing. SQL Server would also refuse a second cascade path from the
    /// recording. A marker id that no longer resolves means the marker is gone.</para>
    /// </remarks>
    public partial class EvpScanCandidate
    {
        public Guid Id { get; set; }

        public Guid EvpScanId { get; set; }

        /// <summary>The Pending marker this became; null when it was not proposed.</summary>
        public Guid? AudioMarkerId { get; set; }

        /// <summary>False when it overlapped a marker somebody had already kept, dismissed or placed by hand.</summary>
        public bool Proposed { get; set; }

        /// <summary>The span as proposed, context padding included.</summary>
        public double StartSeconds { get; set; }
        public double EndSeconds { get; set; }

        /// <summary>The 0–100 score the reviewer saw.</summary>
        public float Score { get; set; }

        // What the detector measured — see EvpFeatures for what each one means.
        public double PeakProminenceDb { get; set; }
        public double MeanBandGapDb { get; set; }
        public double EventSeconds { get; set; }
        public double MeanFloorDb { get; set; }
        public double PeakBandDb { get; set; }
        public double BandLevelSpreadDb { get; set; }
        public double ZeroCrossingRate { get; set; }

        public virtual EvpScan EvpScan { get; set; } = null!;
    }

    /// <summary>
    /// One Keep or Dismiss on a detected candidate, as it happened (item 253).
    /// </summary>
    /// <remarks>
    /// <para><b>Append-only.</b> The marker holds only the latest ruling and is deleted when a
    /// person deletes it. This keeps every ruling, including a change of mind, and survives the
    /// marker. It records only rulings on candidates the detector proposed; a marker a person placed
    /// by hand is not a ruling on the detector.</para>
    ///
    /// <para><b>No person is recorded.</b> Who ruled is not needed to learn from the ruling. The
    /// marker's own UpdatedBy already says who last touched it while it exists.</para>
    /// </remarks>
    public partial class EvpRuling
    {
        public Guid Id { get; set; }

        public Guid UploadFileId { get; set; }

        /// <summary>The candidate's marker. No foreign key, so the ruling outlives the marker.</summary>
        public Guid AudioMarkerId { get; set; }

        /// <summary>Confirmed (kept) or Dismissed.</summary>
        public EvpReviewStatus Ruling { get; set; }

        /// <summary>
        /// Whether the reviewer had heard the candidate before ruling: pressed its play button, or
        /// was playing when the playhead entered it. Null when the caller did not say. A Dismiss
        /// made without listening says much less about the sound than one made after.
        /// </summary>
        public bool? PlayedFirst { get; set; }

        /// <summary>
        /// The marker's span at the ruling differs from what the detector proposed: the reviewer
        /// moved the edges, so the detector's span was off.
        /// </summary>
        public bool BoundsAdjusted { get; set; }

        /// <summary>The detector's score on the marker at the time, so the ruling reads on its own.</summary>
        public float? DetectionScore { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
    }
}
