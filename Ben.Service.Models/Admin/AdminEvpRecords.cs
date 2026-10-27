namespace Ben.Service.Models.Admin;

/// <summary>
/// What the EVP scanner has done and what people made of it (audio phase 11, item 253): the record the
/// learning tables keep, read back for the first time.
/// </summary>
/// <param name="Days">The period, ending now.</param>
/// <param name="Proposed">Candidates that reached somebody's review list.</param>
/// <param name="Exploratory">Of those, how many were lower-ranked samples (phase 5).</param>
/// <param name="PlayedFirstShare">Share of rulings made after listening, 0–1; null with no rulings.</param>
/// <param name="BlindShare">Share of rulings made blind, 0–1; null with no rulings.</param>
/// <param name="KeptShareBlind">Of blind rulings, the share kept; null with none.</param>
/// <param name="KeptShareShown">Of rulings made with the readings shown, the share kept; null with none.</param>
/// <param name="Misses">Hand-placed markers where the latest scan had nothing.</param>
/// <param name="FeatureHistograms">For each measurement, kept and dismissed candidates side by side.</param>
public sealed record AdminEvpSummary(
    int Days,
    int Scans,
    int Candidates,
    int Proposed,
    int Exploratory,
    int Rulings,
    int Kept,
    int Dismissed,
    double? PlayedFirstShare,
    double? BlindShare,
    double? KeptShareBlind,
    double? KeptShareShown,
    int Misses,
    IReadOnlyList<AdminEvpDay> ScansPerDay,
    IReadOnlyList<AdminEvpHistogram> FeatureHistograms);

/// <param name="Day">The day, as yyyy-MM-dd.</param>
public sealed record AdminEvpDay(string Day, int Scans);

/// <param name="Feature">What was measured, in words.</param>
/// <param name="Measured">How many ruled candidates had this measurement.</param>
public sealed record AdminEvpHistogram(string Feature, string Unit, int Measured, IReadOnlyList<AdminEvpBin> Bins);

public sealed record AdminEvpBin(double Lo, double Hi, int Kept, int Dismissed);

/// <summary>
/// How EVP candidates are scored (audio phase 13): the weights in use, how far the decisions are from
/// enough to fit, and every fit so far.
/// </summary>
/// <param name="ActiveVersion">1 for the built-in weights.</param>
public sealed record AdminEvpScoring(
    int ActiveVersion,
    AdminEvpWeights Active,
    int Decisions,
    int Recordings,
    int MinimumDecisions,
    int MinimumRecordings,
    IReadOnlyList<AdminEvpFit> Fits);

/// <summary>How much each part of the score counts, 0–1, adding up to 1.</summary>
public sealed record AdminEvpWeights(double StoodOut, double InRange, double Length);

/// <param name="HeldOutAuc">How often a kept candidate outranks a dismissed one on recordings it did not learn from.</param>
/// <param name="BaselineHeldOutAuc">The same for the built-in weights.</param>
public sealed record AdminEvpFit(
    int Version, AdminEvpWeights Weights, int Decisions, int Recordings, int HeldOutDecisions,
    double HeldOutAuc, double BaselineHeldOutAuc, bool PassedGate, string GateNote, bool IsActive,
    DateTime DateCreated, DateTime? ActivatedUtc);
