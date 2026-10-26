using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// An EVP scan running in the background, so a long recording or several of them can be
    /// scanned without anybody waiting on the page (phase 6, 10/26/2026).
    /// </summary>
    /// <remarks>
    /// <para>One job, one or more recordings (<see cref="Segments"/>): the editor's scan is a job of
    /// one, and the batch and field-session scans of later phases are jobs of many. The results
    /// themselves land where a scan's always have, as Pending markers and an <c>EvpScan</c> record;
    /// this row only says how far it got. Finished jobs are deleted after a week.</para>
    ///
    /// <para><b>The person who asked has no cascade.</b> SQL Server refuses a second cascade path to
    /// the segments (person → file → segment, person → job → segment), and a job outliving a deleted
    /// account by up to a week is harmless.</para>
    /// </remarks>
    public partial class EvpScanJob
    {
        public Guid Id { get; set; }
        public Guid RequestedByAppUserId { get; set; }

        /// <summary>The preset picked; <see cref="OptionsJson"/> is what actually runs.</summary>
        public EvpSensitivity Sensitivity { get; set; }

        /// <summary>The <c>EvpDetectionOptions</c> every recording in the job is scanned with.</summary>
        public string OptionsJson { get; set; } = string.Empty;

        public EvpScanJobStatus Status { get; set; }

        /// <summary>0–100, weighted by each recording's length.</summary>
        public int Percent { get; set; }

        /// <summary>Why it failed, in a sentence for the person who asked.</summary>
        public string? Error { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }

        /// <summary>Set by Cancel; the worker stops at the next piece of audio.</summary>
        public DateTime? CancelRequestedUtc { get; set; }

        public virtual AppUser RequestedBy { get; set; } = null!;
        public virtual ICollection<EvpScanJobSegment> Segments { get; set; } = new List<EvpScanJobSegment>();
    }

    /// <summary>One recording in a background scan, and how far it has got.</summary>
    /// <remarks>
    /// At most one unfinished segment per recording, held by a filtered unique index: two scans of
    /// one recording at once would each replace the other's Pending candidates.
    /// </remarks>
    public partial class EvpScanJobSegment
    {
        public Guid Id { get; set; }
        public Guid EvpScanJobId { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>Its place in the job; segments run in this order.</summary>
        public int Order { get; set; }

        public EvpScanJobStatus Status { get; set; }
        public int Percent { get; set; }

        /// <summary>Audio read so far.</summary>
        public double ProcessedSeconds { get; set; }

        /// <summary>Audio whose result can no longer change; the editor draws it solid.</summary>
        public double SettledSeconds { get; set; }

        /// <summary>The recording's length when it is known before reading it.</summary>
        public double? TotalSeconds { get; set; }

        /// <summary>When done: how many candidates it proposed, and the best score among them.</summary>
        public int? CandidateCount { get; set; }
        public float? TopScore { get; set; }

        /// <summary>The <c>EvpScan</c> record it made. No foreign key: the record outlives the job.</summary>
        public Guid? EvpScanId { get; set; }

        public string? Error { get; set; }
        public DateTime? StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }

        public virtual EvpScanJob EvpScanJob { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
    }
}
