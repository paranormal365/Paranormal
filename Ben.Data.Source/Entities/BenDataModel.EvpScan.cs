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

        /// <summary>
        /// Every setting the scan ran with, as JSON (phase 5). The five columns above stay for
        /// the scans made before, which have no JSON; null here means one of those.
        /// </summary>
        public string? OptionsJson { get; set; }

        /// <summary>The listening range, out of the JSON because a re-fit groups by it. Null before phase 5 (300–3400 Hz then).</summary>
        public double? BandLowHz { get; set; }
        public double? BandHighHz { get; set; }

        /// <summary>
        /// What the detector saw, packed by <c>EvpTraceCodec</c> (phase 8): the levels a tenth of a
        /// second at a time and where its gate opened, for the editor's detector lane. Null before
        /// phase 8, or when it could not be kept.
        /// </summary>
        public byte[]? TraceBlob { get; set; }

        /// <summary>The trace's bucket length in milliseconds; null with no trace.</summary>
        public int? TraceBucketMs { get; set; }

        /// <summary>
        /// The voice model that measured this scan's candidates (<c>voicenet-v1</c>), or null when none did.
        /// A retrained model has a new name, so its readings are never mixed with an older one's.
        /// </summary>
        public string? VoiceModel { get; set; }

        /// <summary>
        /// Which weights scored this scan's candidates (phase 13): 1 for the built-in ones, else an
        /// <see cref="EvpScoringVersion"/>. Rulings are only ever compared within one version.
        /// </summary>
        public int ScoringVersion { get; set; } = 1;

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

        /// <summary>Kept as a lower-ranked sample, not for its score (phase 5, item 253).</summary>
        public bool Exploratory { get; set; }

        /// <summary>How voice-shaped it was, 0–1: the eighth feature (VoiceNet, phase 9b). Null when not measured.</summary>
        public float? VoiceLikelihood { get; set; }

        /// <summary>Feature 8 (phase 11): spectral flatness in the listening range, 0–1; hiss is near 1. Null when not measured.</summary>
        public float? SpectralFlatness { get; set; }

        /// <summary>Feature 9 (phase 11): the share of its frames with a speaking voice's pitch, 0–1. Null when not measured.</summary>
        public float? VoicedFrameFraction { get; set; }

        public virtual EvpScan EvpScan { get; set; } = null!;
    }

    /// <summary>
    /// A set of EVP scan settings somebody saved under a name of their own (phase 5, 10/26/2026).
    /// </summary>
    /// <remarks>
    /// <para>One person's, visible to nobody else. At most one is their default, which a recording
    /// with no settings of its own starts from. Deleting the person deletes their presets.</para>
    /// <para><see cref="OptionsJson"/> is an <c>EvpDetectionOptions</c>, checked by the server on
    /// the way in and again on the way out, so a preset saved before a range tightened is refused
    /// with the reason rather than run.</para>
    /// </remarks>
    public partial class EvpScanPreset
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OptionsJson { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
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
        /// Ruled with the scan's readings hidden (blind review, phase 11). Null when the caller did not say.
        /// Comparing blind rulings with shown ones tells whether the score sways reviewers.
        /// </summary>
        public bool? Blind { get; set; }

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

    /// <summary>
    /// A sound the scan did not find and a person did (audio phase 11, item 253, 10/27/2026): a marker placed
    /// by hand on a scanned recording where the latest scan had no candidate.
    /// </summary>
    /// <remarks>
    /// The other half of what a ruling teaches. Keeps and dismisses only ever judge what the detector
    /// proposed; these are what it should have proposed. Only the recording has a foreign key: the scan and
    /// the marker are named, not held, so deleting either leaves the record of the miss.
    /// </remarks>
    public partial class EvpMiss
    {
        public Guid Id { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>The scan that missed it. No foreign key.</summary>
        public Guid EvpScanId { get; set; }

        /// <summary>The hand-placed marker. No foreign key.</summary>
        public Guid AudioMarkerId { get; set; }

        public double StartSeconds { get; set; }
        public double? EndSeconds { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
    }

    /// <summary>
    /// One fit of the score's weights from Keep and Dismiss (audio phase 13, 10/27/2026), kept whether or
    /// not it is ever used.
    /// </summary>
    /// <remarks>
    /// <para>Appended by <c>EvpWeightRefitJob</c>, never edited except to switch on or off. Version 1, the
    /// built-in weights, is not a row: it is what runs when no row is active.</para>
    /// <para><b>Nothing switches on by itself.</b> A fit records whether it passed the gate (enough decisions
    /// from enough recordings, better than version 1 on recordings it did not learn from, and the expected
    /// order of a speech-length sound above a click, a hum and long talking). A SuperAdmin switches a passing
    /// one on, and can go back to the built-in weights at any time.</para>
    /// </remarks>
    public partial class EvpScoringVersion
    {
        public Guid Id { get; set; }

        /// <summary>2, 3, …: the number scans record.</summary>
        public int Version { get; set; }

        public double WeightProminence { get; set; }
        public double WeightBand { get; set; }
        public double WeightDuration { get; set; }

        /// <summary>Decisions and recordings it learned from and was tested on, together.</summary>
        public int Decisions { get; set; }
        public int Recordings { get; set; }

        /// <summary>Decisions held back to test it: those on every fifth recording.</summary>
        public int HeldOutDecisions { get; set; }

        /// <summary>How often a kept candidate outranks a dismissed one (0.5 is chance, 1 is perfect).</summary>
        public double TrainAuc { get; set; }
        public double HeldOutAuc { get; set; }

        /// <summary>The same measure for the built-in weights on the same held-back decisions.</summary>
        public double BaselineHeldOutAuc { get; set; }

        public bool PassedGate { get; set; }

        /// <summary>What the gate said, in a sentence.</summary>
        public string GateNote { get; set; } = string.Empty;

        /// <summary>At most one is active, held by a filtered unique index.</summary>
        public bool IsActive { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? ActivatedUtc { get; set; }
        public Guid? ActivatedByAppUserId { get; set; }
    }
}

