namespace Ben.Service.Models.Entities;

/// <summary>
/// What the detector saw across a recording, for the editor's detector lane (phase 8, 10/26/2026).
/// </summary>
/// <param name="BucketSeconds">How much of the recording each level stands for.</param>
/// <param name="ThresholdDb">The scan's threshold: the line is the background plus this.</param>
/// <param name="ReleaseDb">How far a sound fell before it ended: the background plus this.</param>
/// <param name="BandDb">The loudest voice-band level in each bucket, in dB.</param>
/// <param name="FloorDb">The room's background in each bucket, in dB.</param>
/// <param name="GateRuns">Where the detector's gate opened, before joining and the shortest-sound rule.</param>
public sealed record EvpTraceRecord(
    Guid ScanId,
    DateTime ScannedUtc,
    double BucketSeconds,
    double ThresholdDb,
    double ReleaseDb,
    double RecordingSeconds,
    float[] BandDb,
    float[] FullDb,
    float[] FloorDb,
    IReadOnlyList<EvpSpanRecord> GateRuns);

/// <summary>A stretch of a recording, in seconds.</summary>
public sealed record EvpSpanRecord(double Start, double End);

/// <summary>
/// Why the detector proposed one candidate (phase 8): its score taken apart, and what it measured.
/// </summary>
public sealed record EvpCandidateDetailRecord(
    float Score,
    IReadOnlyList<EvpScoreTermRecord> Terms,
    double PeakProminenceDb,
    double MeanFloorDb,
    double PeakBandDb,
    double EventSeconds,
    double BandLevelSpreadDb,
    double ZeroCrossingRate);

/// <summary>One part of a score: how many of its points it earned, and why, in a sentence.</summary>
public sealed record EvpScoreTermRecord(string Name, double Points, double MaxPoints, string Sentence);
