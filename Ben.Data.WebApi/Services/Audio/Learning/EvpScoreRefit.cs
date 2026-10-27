namespace Ben.Data.WebApi.Services.Audio.Learning;

/// <summary>One decision to learn from: the candidate's measurements and whether it was kept.</summary>
/// <param name="RecordingId">The recording it was on; held-back decisions are whole recordings.</param>
/// <param name="Weight">How much it counts: 2 for a decision made after listening, else 1.</param>
public sealed record EvpDecision(Guid RecordingId, EvpFeatures Features, bool Kept, double Weight = 1);

/// <summary>What a fit found, and whether it may be switched on.</summary>
public sealed record EvpRefitResult(
    EvpScoreWeights Weights, int Decisions, int Recordings, int HeldOutDecisions,
    double TrainAuc, double HeldOutAuc, double BaselineHeldOutAuc, bool PassedGate, string GateNote);

/// <summary>
/// Fits the score's three weights to people's Keep and Dismiss decisions (audio phase 13, 10/27/2026).
/// </summary>
/// <remarks>
/// <para><b>What it learns.</b> Only how much each existing part of the score counts: how far a sound
/// stood out, how much was in the listening range, and its length. Each weight stays at or above zero
/// and they add up to 1, so a score is still 0–100, still made of the same three parts, and Why? still
/// explains it. Every combination in steps of 0.05 is tried (231 of them) and the one that most often
/// ranks a kept candidate above a dismissed one wins. Small and exhaustive on purpose: three numbers a
/// person can read and judge, not a model nobody can.</para>
///
/// <para><b>How it is judged.</b> Decisions on every fifth recording (by its id, so always the same ones)
/// are held back and never learned from. On those, the fit must rank kept above dismissed better than
/// the built-in weights by at least <see cref="MinimumImprovement"/>, it must still put a speech-length
/// sound above a click, a steady hum and long talking (<see cref="KeepsTheExpectedOrder"/>), and there
/// must be at least <see cref="MinimumDecisions"/> decisions from <see cref="MinimumRecordings"/>
/// recordings. A decision made after listening counts twice, as the editor's "How it works" promises.</para>
/// </remarks>
public static class EvpScoreRefit
{
    public const int MinimumDecisions = 500;
    public const int MinimumRecordings = 20;
    public const double MinimumImprovement = 0.01;
    public const double Step = 0.05;

    /// <summary>Fits the weights, or returns null when there is nothing of both kinds to learn from.</summary>
    public static EvpRefitResult? Fit(IReadOnlyList<EvpDecision> decisions, int version)
    {
        var heldOut = decisions.Where(d => IsHeldOut(d.RecordingId)).ToList();
        var train = decisions.Where(d => !IsHeldOut(d.RecordingId)).ToList();
        if (!HasBoth(train)) return null;

        var parts = train.Select(d => (Parts: EvpDetector.Parts(d.Features), d.Kept, d.Weight)).ToList();
        var best = EvpScoreWeights.BuiltIn;
        var bestAuc = double.MinValue;
        var steps = (int)Math.Round(1 / Step);
        for (var i = 0; i <= steps; i++)
            for (var j = 0; j <= steps - i; j++)
            {
                var w = new EvpScoreWeights(version, i * Step, j * Step, (steps - i - j) * Step);
                var auc = Auc(parts.Select(p => (Score(p.Parts, w), p.Kept, p.Weight)));
                // Ties go to the weights nearest the built-in ones: no change without a reason.
                if (auc > bestAuc + 1e-12 || (Math.Abs(auc - bestAuc) <= 1e-12 && Distance(w) < Distance(best)))
                {
                    best = w;
                    bestAuc = auc;
                }
            }

        var recordings = decisions.Select(d => d.RecordingId).Distinct().Count();
        var heldOutAuc = HasBoth(heldOut) ? AucOf(heldOut, best) : double.NaN;
        var baseline = HasBoth(heldOut) ? AucOf(heldOut, EvpScoreWeights.BuiltIn) : double.NaN;

        var note =
            decisions.Count < MinimumDecisions ? $"Needs {MinimumDecisions} decisions; there are {decisions.Count}."
            : recordings < MinimumRecordings ? $"Needs decisions from {MinimumRecordings} recordings; they come from {recordings}."
            : !HasBoth(heldOut) ? "The held-back recordings have no kept and dismissed decisions to test on."
            : heldOutAuc < baseline + MinimumImprovement
                ? $"Not better than the built-in weights on the held-back recordings ({heldOutAuc:0.000} against {baseline:0.000})."
            : !KeepsTheExpectedOrder(best) ? "It would rank a click, a hum or long talking above a speech-length sound."
            : $"Better than the built-in weights on the held-back recordings ({heldOutAuc:0.000} against {baseline:0.000}).";
        var passed = note.StartsWith("Better", StringComparison.Ordinal);

        return new EvpRefitResult(best, decisions.Count, recordings, heldOut.Count,
            bestAuc, heldOutAuc, baseline, passed, note);
    }

    /// <summary>Every fifth recording, always the same ones.</summary>
    public static bool IsHeldOut(Guid recordingId) => recordingId.ToByteArray()[0] % 5 == 0;

    /// <summary>
    /// The order the detector's own tests are built on, kept by any weights that may be switched on: a
    /// speech-length sound in the listening range outranks a loud wideband click, a steady hum lasting
    /// half a minute, and a stretch of somebody talking.
    /// </summary>
    public static bool KeepsTheExpectedOrder(EvpScoreWeights w)
    {
        var utterance = EvpDetector.Score(new EvpFeatures(18, -1, 0.8, 0, 0, 0, 0), w);
        var click     = EvpDetector.Score(new EvpFeatures(25, -12, 0.06, 0, 0, 0, 0), w);
        var hum       = EvpDetector.Score(new EvpFeatures(12, -0.5, 30, 0, 0, 0, 0), w);
        var talking   = EvpDetector.Score(new EvpFeatures(15, -1, 12, 0, 0, 0, 0), w);
        return utterance > click && utterance > hum && utterance > talking;
    }

    /// <summary>
    /// How often a kept decision outranks a dismissed one, each pair counted by both decisions'
    /// weights; ties count half. 0.5 is chance.
    /// </summary>
    public static double Auc(IEnumerable<(double Score, bool Kept, double Weight)> scored)
    {
        var sorted = scored.OrderBy(s => s.Score).ToList();
        double keptTotal = 0, dismissedTotal = 0, dismissedBelow = 0, wins = 0;
        foreach (var s in sorted) { if (s.Kept) keptTotal += s.Weight; else dismissedTotal += s.Weight; }
        if (keptTotal == 0 || dismissedTotal == 0) return double.NaN;

        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            double keptHere = 0, dismissedHere = 0;
            while (j < sorted.Count && sorted[j].Score == sorted[i].Score)
            {
                if (sorted[j].Kept) keptHere += sorted[j].Weight; else dismissedHere += sorted[j].Weight;
                j++;
            }
            wins += keptHere * (dismissedBelow + 0.5 * dismissedHere);
            dismissedBelow += dismissedHere;
            i = j;
        }
        return wins / (keptTotal * dismissedTotal);
    }

    private static double AucOf(IEnumerable<EvpDecision> decisions, EvpScoreWeights w)
        => Auc(decisions.Select(d => ((double)EvpDetector.Score(d.Features, w), d.Kept, d.Weight)));

    private static double Score((double P, double B, double D) parts, EvpScoreWeights w)
        => w.Prominence * parts.P + w.Band * parts.B + w.Duration * parts.D;

    private static bool HasBoth(IReadOnlyCollection<EvpDecision> decisions)
        => decisions.Any(d => d.Kept) && decisions.Any(d => !d.Kept);

    private static double Distance(EvpScoreWeights w)
        => Math.Abs(w.Prominence - 0.5) + Math.Abs(w.Band - 0.3) + Math.Abs(w.Duration - 0.2);
}
