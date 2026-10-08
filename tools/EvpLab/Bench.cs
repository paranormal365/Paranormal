using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Audio;
using Ben.Service.Models.Entities;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace EvpLab;

/// <summary>One clip Whisper was given, what it is known to contain, and what Whisper said.</summary>
internal sealed record Judged(string Set, string File, double Start, double End, string Truth, double? LevelDb, string? Phrase,
                              string Text, string Heard, float Probability, float MinProbability, bool HeardWords, bool Correct, double Seconds);

/// <summary>Grades detectors against the corpus and writes one report every detector is measured by.</summary>
internal static partial class Bench
{
    public static string Run(string corpusDir, string? whisperModel, string? vadModel, string? voiceNetModel, int threads, int noiseWindowsPerControl)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(corpusDir, "manifest.json")))!;
        var audio = manifest.Files.ToDictionary(f => f.Name, f => Audio.ReadMono16k(Path.Combine(corpusDir, f.Name + ".wav")));
        var report = new StringBuilder();
        var minutes = manifest.Files.Count * manifest.SecondsPerFile / 60;
        var controlMinutes = manifest.Files.Count(f => f.Events.Count == 0) * manifest.SecondsPerFile / 60;

        report.AppendLine($"# EVP bench report — {DateTime.Now:MM/dd/yyyy h:mm tt}");
        report.AppendLine();
        report.AppendLine($"Corpus seed {manifest.Seed}: {manifest.Files.Count} files, {minutes:0.#} minutes " +
                          $"({manifest.Files.Sum(f => f.Events.Count(e => e.Kind == "speech"))} placed voices, " +
                          $"{manifest.Files.Sum(f => f.Events.Count(e => e.Kind == "impostor"))} impostors, {controlMinutes:0.#} minutes of noise alone). " +
                          "Backgrounds: " + string.Join(", ", manifest.Files.GroupBy(f => f.Background).Select(g => $"{g.Key} {g.Count()}")) + ".");
        report.AppendLine();
        report.AppendLine("Level = the placed sound's RMS against the background's over the same stretch, both measured in the voice band (300–3400 Hz). 0 dB is as loud as the noise where a voice lives.");
        report.AppendLine();

        // ── Stage 1: today's EvpDetector ───────────────────────────────────────────
        report.AppendLine("## 1. Today's EvpDetector (energy in the voice band)");
        report.AppendLine();
        report.AppendLine("| Preset | " + string.Join(" | ", Corpus.SpeechLevels.Select(l => $"voices caught at {l:+0;-0} dB")) +
                          " | impostors flagged | false alarms / min (all) | false alarms / min (noise-only files) | ms per min of audio |");
        report.AppendLine("|---|" + string.Concat(Corpus.SpeechLevels.Select(_ => "---|")) + "---|---|---|---|");

        IReadOnlyDictionary<string, IReadOnlyList<EvpCandidate>>? highCandidates = null;
        foreach (var preset in new[] { EvpSensitivity.Low, EvpSensitivity.Medium, EvpSensitivity.High })
        {
            var options = EvpDetectionOptions.FromSensitivity(preset);
            var sw = Stopwatch.StartNew();
            var found = manifest.Files.ToDictionary(f => f.Name, f => EvpDetector.Detect(audio[f.Name], Audio.Rate, options, 500));
            sw.Stop();
            if (preset == EvpSensitivity.High) highCandidates = found;

            var caught = Corpus.SpeechLevels.Select(level =>
            {
                var events = manifest.Files.SelectMany(f => f.Events.Where(e => e.Kind == "speech" && e.LevelDb == level).Select(e => (f, e))).ToList();
                return (Hit: events.Count(x => found[x.f.Name].Any(c => Overlaps(c, x.e))), Of: events.Count);
            }).ToList();
            var impostors = manifest.Files.SelectMany(f => f.Events.Where(e => e.Kind == "impostor").Select(e => (f, e))).ToList();
            var impostorHits = impostors.Count(x => found[x.f.Name].Any(c => Overlaps(c, x.e)));
            var falseAll = manifest.Files.Sum(f => found[f.Name].Count(c => !f.Events.Any(e => Overlaps(c, e))));
            var falseControl = manifest.Files.Where(f => f.Events.Count == 0).Sum(f => found[f.Name].Count);

            report.AppendLine($"| {preset} | " + string.Join(" | ", caught.Select(c => $"{c.Hit}/{c.Of}")) +
                              $" | {impostorHits}/{impostors.Count} | {falseAll / minutes:0.0} | {falseControl / controlMinutes:0.0} | {sw.Elapsed.TotalMilliseconds / minutes:0} |");
        }
        report.AppendLine();
        report.AppendLine("Impostors flagged is expected, not a fault: the detector finds sound, and a knock is sound. Telling a voice from a knock is the job of the next stage.");
        report.AppendLine();

        if (vadModel is not null && File.Exists(vadModel))
        {
            using var vad = new SileroVad(vadModel);
            ScorerStage(report, manifest, audio, highCandidates!, vad, noiseWindowsPerControl, minutes, controlMinutes);
        }
        else
            report.AppendLine($"_Voice-activity detector not graded: no model at `{vadModel ?? "(none given)"}`._\n");

        if (voiceNetModel is not null && File.Exists(voiceNetModel))
        {
            using var net = new VoiceNet(voiceNetModel);
            ScorerStage(report, manifest, audio, highCandidates!, net, noiseWindowsPerControl, minutes, controlMinutes);
        }

        if (whisperModel is null || !File.Exists(whisperModel))
        {
            report.AppendLine($"_Whisper not graded: no model at `{whisperModel ?? "(none given)"}`._");
            return report.ToString();
        }

        // ── Stage 2: Whisper small ────────────────────────────────────────────────
        var judged = new List<Judged>();
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
        using var factory = WhisperFactory.FromPath(whisperModel, new WhisperFactoryOptions { UseGpu = false });
        using var processor = factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(threads)
            .WithNoContext()
            .WithProbabilities()
            .Build();

        Judged Judge(string set, CorpusFile file, float[] clip, double start, double end, string truth, PlacedEvent? e)
        {
            var sw = Stopwatch.StartNew();
            var segments = processor.ProcessAsync(clip).ToBlockingEnumerable().ToList();
            sw.Stop();
            var text = string.Concat(segments.Select(s => s.Text)).Trim();
            var heard = Normalise(text);
            var probability = segments.Count == 0 ? 0f : segments.Average(s => s.Probability);
            var minProbability = segments.Count == 0 ? 0f : segments.Min(s => s.MinProbability);
            var correct = e?.Phrase is { } phrase && Normalise(phrase).Split(' ').All(w => heard.Split(' ').Contains(w));
            return new Judged(set, file.Name, start, end, truth, e?.LevelDb, e?.Phrase, text, heard, probability, minProbability, heard.Length > 0, correct, sw.Elapsed.TotalSeconds);
        }

        Console.Error.WriteLine($"Whisper runtime: {RuntimeOptions.LoadedLibrary}");
        var rng = new Random(manifest.Seed);

        // 2a. Each placed voice on its own, forwards and reversed — what Whisper can do regardless of stage 1.
        foreach (var f in manifest.Files)
            foreach (var e in f.Events.Where(e => e.Kind == "speech"))
            {
                var (a, b) = Widen(e.Start - 0.3, e.End + 0.3, manifest.SecondsPerFile);
                var clip = Audio.Slice(audio[f.Name], a, b);
                judged.Add(Judge("voice", f, clip, a, b, "speech", e));
                var reversed = (float[])clip.Clone(); Array.Reverse(reversed);
                judged.Add(Judge("voice-reversed", f, reversed, a, b, "reversed speech", e) with { Correct = false });
            }
        Console.Error.WriteLine($"  voices done ({judged.Count} clips)");

        // 2b. Noise alone: random 2-second windows from the files with nothing placed in them.
        foreach (var f in manifest.Files.Where(f => f.Events.Count == 0))
            for (var i = 0; i < noiseWindowsPerControl; i++)
            {
                var a = rng.NextDouble() * (manifest.SecondsPerFile - 2);
                judged.Add(Judge("noise", f, Audio.Slice(audio[f.Name], a, a + 2), a, a + 2, "noise", null));
            }
        Console.Error.WriteLine($"  noise done ({judged.Count} clips)");

        // 2c. The real pipeline: everything the High preset flagged, as an investigator's queue would hold it.
        foreach (var f in manifest.Files)
            foreach (var c in highCandidates![f.Name])
            {
                var (a, b) = Widen(c.StartSeconds, c.EndSeconds, manifest.SecondsPerFile);
                var e = f.Events.FirstOrDefault(e => e.Kind == "speech" && Overlaps(c, e)) ?? f.Events.FirstOrDefault(e => Overlaps(c, e));
                var truth = e?.Kind ?? "noise";
                judged.Add(Judge("queue", f, Audio.Slice(audio[f.Name], a, b), a, b, truth, e));
            }
        Console.Error.WriteLine($"  queue done ({judged.Count} clips)");

        File.WriteAllLines(Path.Combine(corpusDir, "whisper-judged.jsonl"), judged.Select(j => JsonSerializer.Serialize(j)));
        WriteWhisperReport(report, judged, threads);
        return report.ToString();
    }

    private static void WriteWhisperReport(StringBuilder report, List<Judged> judged, int threads)
    {
        report.AppendLine($"## 2. Whisper small, on this machine's CPU ({threads} threads, no GPU; runtime `{RuntimeOptions.LoadedLibrary}`)");
        report.AppendLine();
        report.AppendLine("\"Words\" = Whisper wrote any words at all. \"Right\" = every word of the placed phrase is in what it wrote.");
        report.AppendLine();

        report.AppendLine("### 2a. Each placed voice, cut out and given straight to Whisper");
        report.AppendLine();
        report.AppendLine("| Level | words | right | words from the same clip reversed |");
        report.AppendLine("|---|---|---|---|");
        foreach (var level in Corpus.SpeechLevels)
        {
            var fwd = judged.Where(j => j.Set == "voice" && j.LevelDb == level).ToList();
            var rev = judged.Where(j => j.Set == "voice-reversed" && j.LevelDb == level).ToList();
            report.AppendLine($"| {level:+0;-0} dB | {Pct(fwd, j => j.HeardWords)} | {Pct(fwd, j => j.Correct)} | {Pct(rev, j => j.HeardWords)} |");
        }
        report.AppendLine();

        var noise = judged.Where(j => j.Set == "noise").ToList();
        report.AppendLine("### 2b. Noise alone (2-second windows, nothing placed)");
        report.AppendLine();
        report.AppendLine($"Whisper wrote words for **{Pct(noise, j => j.HeardWords)}** of {noise.Count} windows of pure noise.");
        var invented = noise.Where(j => j.HeardWords).GroupBy(j => j.Heard).OrderByDescending(g => g.Count()).Take(12).ToList();
        if (invented.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("What it wrote, most frequent first:");
            report.AppendLine();
            foreach (var g in invented) report.AppendLine($"- \"{g.First().Text}\" ×{g.Count()} (confidence {g.Average(j => j.Probability):0.00})");
        }
        report.AppendLine();

        report.AppendLine("### 2c. The queue an investigator would get: every High-preset candidate, through Whisper");
        report.AppendLine();
        report.AppendLine("| Candidate actually contains | clips | words | right |");
        report.AppendLine("|---|---|---|---|");
        foreach (var g in judged.Where(j => j.Set == "queue").GroupBy(j => j.Truth))
            report.AppendLine($"| {g.Key} | {g.Count()} | {Pct(g, j => j.HeardWords)} | {(g.Key == "speech" ? Pct(g, j => j.Correct) : "—")} |");
        report.AppendLine();

        report.AppendLine("### 2d. Does Whisper's own confidence separate voices from invention?");
        report.AppendLine();
        report.AppendLine("Showing a suggestion only when confidence is at least the threshold:");
        report.AppendLine();
        report.AppendLine("| Threshold | real voices shown and right (all levels) | at −12 dB and below | noise windows shown words | reversed voices shown words |");
        report.AppendLine("|---|---|---|---|---|");
        var voices = judged.Where(j => j.Set == "voice").ToList();
        var quiet = voices.Where(j => j.LevelDb <= -12).ToList();
        var reversed = judged.Where(j => j.Set == "voice-reversed").ToList();
        foreach (var t in new[] { 0f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f })
            report.AppendLine($"| {t:0.0} | {Pct(voices, j => j.Correct && j.Probability >= t)} | {Pct(quiet, j => j.Correct && j.Probability >= t)} | " +
                              $"{Pct(noise, j => j.HeardWords && j.Probability >= t)} | {Pct(reversed, j => j.HeardWords && j.Probability >= t)} |");
        report.AppendLine();

        var audioSeconds = judged.Sum(j => j.End - j.Start);
        var seconds = judged.Sum(j => j.Seconds);
        report.AppendLine("### 2e. Cost");
        report.AppendLine();
        report.AppendLine($"{judged.Count} clips, {audioSeconds:0} s of audio, {seconds:0} s of Whisper: **{seconds / judged.Count:0.00} s per clip**, " +
                          $"{seconds / audioSeconds:0.00} s per second of audio. This is an Apple M-series CPU; a Windows server CPU will be slower — measure there before promising a wait.");
    }

    // ── Stage 1b: frame scorers (Silero VAD, VoiceNet) ─────────────────────────────
    private sealed record ScoredClip(string Set, CorpusFile File, PlacedEvent? Event, string Truth, float[] Scores);

    private static void ScorerStage(StringBuilder report, Manifest manifest, Dictionary<string, float[]> audio,
        IReadOnlyDictionary<string, IReadOnlyList<EvpCandidate>> high, IFrameScorer scorer, int noiseWindowsPerControl, double minutes, double controlMinutes)
    {
        var clips = new List<ScoredClip>();
        var rng = new Random(manifest.Seed);   // the same noise windows Whisper is given
        var sw = Stopwatch.StartNew();
        double clipSeconds = 0;

        ScoredClip Read(string set, CorpusFile f, double a, double b, string truth, PlacedEvent? e, bool reverse = false)
        {
            var clip = Audio.Slice(audio[f.Name], a, b);
            if (reverse) Array.Reverse(clip);
            Audio.PeakNormalise(clip);
            clipSeconds += b - a;
            return new ScoredClip(set, f, e, truth, scorer.Score(clip));
        }

        foreach (var f in manifest.Files)
            foreach (var e in f.Events)
            {
                var (a, b) = Widen(e.Start - 0.3, e.End + 0.3, manifest.SecondsPerFile);
                if (e.Kind == "speech")
                {
                    clips.Add(Read("voice", f, a, b, "speech", e));
                    clips.Add(Read("voice-reversed", f, a, b, "reversed speech", e, reverse: true));
                }
                else clips.Add(Read("impostor", f, a, b, "impostor", e));
            }
        foreach (var f in manifest.Files.Where(f => f.Events.Count == 0))
            for (var i = 0; i < noiseWindowsPerControl; i++)
            {
                var a = rng.NextDouble() * (manifest.SecondsPerFile - 2);
                clips.Add(Read("noise", f, a, a + 2, "noise", null));
            }
        foreach (var f in manifest.Files)
            foreach (var c in high[f.Name])
            {
                var (a, b) = Widen(c.StartSeconds, c.EndSeconds, manifest.SecondsPerFile);
                var e = f.Events.FirstOrDefault(e => e.Kind == "speech" && Overlaps(c, e)) ?? f.Events.FirstOrDefault(e => Overlaps(c, e));
                clips.Add(Read("queue", f, a, b, e?.Kind ?? "noise", e));
            }
        sw.Stop();

        report.AppendLine($"## {scorer.Title}");
        report.AppendLine();
        report.AppendLine(scorer.RuleText + " A reversed voice is still a human voice, so flagging it is correct; what must not be flagged is noise and knocks. " +
                          "Every clip is peak-normalised to −1 dBFS first, as the product already does for clips.");
        report.AppendLine();
        report.AppendLine("| T | R | " + string.Join(" | ", Corpus.SpeechLevels.Select(l => $"voices {l:+0;-0} dB")) +
                          " | reversed voices | impostors | noise windows | High queue: voices kept | High queue: non-voices kept |");
        report.AppendLine("|---|---|" + string.Concat(Corpus.SpeechLevels.Select(_ => "---|")) + "---|---|---|---|---|");
        foreach (var (t, r) in scorer.Rules)
        {
            bool IsVoice(ScoredClip c) => Holds(c.Scores, t, r, scorer.StepSeconds);
            var voices = Corpus.SpeechLevels.Select(l => Pct(clips.Where(c => c.Set == "voice" && c.Event!.LevelDb == l), IsVoice));
            var queue = clips.Where(c => c.Set == "queue").ToList();
            report.AppendLine($"| {t:0.0} | {r} | " + string.Join(" | ", voices) +
                              $" | {Pct(clips.Where(c => c.Set == "voice-reversed"), IsVoice)} | {Pct(clips.Where(c => c.Set == "impostor"), IsVoice)}" +
                              $" | {Pct(clips.Where(c => c.Set == "noise"), IsVoice)} | {Pct(queue.Where(c => c.Truth == "speech"), IsVoice)}" +
                              $" | {Pct(queue.Where(c => c.Truth != "speech"), IsVoice)} |");
        }
        report.AppendLine();
        var (ft, fr) = scorer.FileRule;
        report.AppendLine($"Impostors by kind at T {ft:0.0}, R {fr}: " + string.Join(", ", clips.Where(c => c.Set == "impostor").GroupBy(c => c.Event!.Subtype)
            .Select(g => $"{g.Key} {Pct(g, c => Holds(c.Scores, ft, fr, scorer.StepSeconds))}")) + ".");
        report.AppendLine();

        // Over whole files, as a first stage in its own right, graded exactly like EvpDetector above.
        var fileSw = Stopwatch.StartNew();
        var regions = manifest.Files.ToDictionary(f => f.Name, f =>
        {
            var whole = (float[])audio[f.Name].Clone();
            Audio.PeakNormalise(whole);
            return Regions(scorer.Score(whole), ft, fr, scorer.StepSeconds, scorer.WindowSeconds, 0.35);
        });
        fileSw.Stop();
        report.AppendLine($"Over whole files as the first stage (T {ft:0.0}, R {fr} ms, gaps under 0.35 s merged):");
        report.AppendLine();
        report.AppendLine("| " + string.Join(" | ", Corpus.SpeechLevels.Select(l => $"voices found at {l:+0;-0} dB")) +
                          " | impostors flagged | false alarms / min (all) | false alarms / min (noise-only files) | ms per min of audio |");
        report.AppendLine("|" + string.Concat(Corpus.SpeechLevels.Select(_ => "---|")) + "---|---|---|---|");
        var found = Corpus.SpeechLevels.Select(level =>
        {
            var events = manifest.Files.SelectMany(f => f.Events.Where(e => e.Kind == "speech" && e.LevelDb == level).Select(e => (f, e))).ToList();
            return $"{events.Count(x => regions[x.f.Name].Any(g => g.Start < x.e.End && g.End > x.e.Start))}/{events.Count}";
        });
        var impostors = manifest.Files.SelectMany(f => f.Events.Where(e => e.Kind == "impostor").Select(e => (f, e))).ToList();
        var impostorHits = impostors.Count(x => regions[x.f.Name].Any(g => g.Start < x.e.End && g.End > x.e.Start));
        var falseAll = manifest.Files.Sum(f => regions[f.Name].Count(g => !f.Events.Any(e => g.Start < e.End && g.End > e.Start)));
        var falseControl = manifest.Files.Where(f => f.Events.Count == 0).Sum(f => regions[f.Name].Count);
        report.AppendLine("| " + string.Join(" | ", found) + $" | {impostorHits}/{impostors.Count} | {falseAll / minutes:0.0} | {falseControl / controlMinutes:0.0} | {fileSw.Elapsed.TotalMilliseconds / minutes:0} |");
        report.AppendLine();
        report.AppendLine($"Cost on clips: {clips.Count} clips, {clipSeconds:0} s of audio in {sw.Elapsed.TotalSeconds:0.0} s.");
        report.AppendLine();
    }

    /// <summary>True when the score stays at or above <paramref name="threshold"/> for at least <paramref name="runMs"/> (and at least one step).</summary>
    private static bool Holds(float[] scores, float threshold, int runMs, double stepSeconds)
    {
        int best = 0, run = 0;
        foreach (var p in scores) { run = p >= threshold ? run + 1 : 0; best = Math.Max(best, run); }
        return best >= Math.Max(1, (int)Math.Ceiling(runMs / (stepSeconds * 1000)));
    }

    private static List<(double Start, double End)> Regions(float[] scores, float threshold, int minMs, double step, double window, double mergeGapSeconds)
    {
        var raw = new List<(double Start, double End)>();
        for (int i = 0; i < scores.Length;)
        {
            if (scores[i] < threshold) { i++; continue; }
            var j = i;
            while (j < scores.Length && scores[j] >= threshold) j++;
            if (j - i >= Math.Max(1, (int)Math.Ceiling(minMs / (step * 1000)))) raw.Add((i * step, (j - 1) * step + window));
            i = j;
        }
        var merged = new List<(double Start, double End)>();
        foreach (var g in raw)
            if (merged.Count > 0 && g.Start - merged[^1].End < mergeGapSeconds) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, g.End));
            else merged.Add(g);
        return merged;
    }

    private static bool Overlaps(EvpCandidate c, PlacedEvent e) => c.StartSeconds < e.End && c.EndSeconds > e.Start;

    /// <summary>Whisper needs a second or so to work with; short candidates are widened evenly within the file.</summary>
    private static (double, double) Widen(double a, double b, double fileSeconds)
    {
        const double min = 1.2;
        if (b - a < min) { var pad = (min - (b - a)) / 2; a -= pad; b += pad; }
        if (a < 0) (a, b) = (0, Math.Min(fileSeconds, b - a));
        if (b > fileSeconds) (a, b) = (Math.Max(0, a - (b - fileSeconds)), fileSeconds);
        return (a, b);
    }

    private static string Pct<T>(IEnumerable<T> items, Func<T, bool> test)
    {
        var list = items.ToList();
        return list.Count == 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * list.Count(test) / list.Count:0}% ({list.Count(test)}/{list.Count})");
    }

    /// <summary>Lower case, contractions opened, bracketed non-speech tags and punctuation removed.</summary>
    public static string Normalise(string text)
    {
        var t = NonSpeechTag().Replace(text.ToLowerInvariant(), " ");
        t = t.Replace("i'm", "i am").Replace("who's", "who is").Replace("’", "'");
        t = NotWord().Replace(t, " ");
        return Spaces().Replace(t, " ").Trim();
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*|♪")] private static partial Regex NonSpeechTag();
    [GeneratedRegex(@"[^a-z0-9' ]")] private static partial Regex NotWord();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
