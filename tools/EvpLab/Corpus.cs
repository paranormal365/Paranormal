using System.Diagnostics;
using System.Text.Json;

namespace EvpLab;

/// <summary>One thing placed in a file on purpose, so its answer is known.</summary>
internal sealed record PlacedEvent(string Kind, string Subtype, double Start, double End, double LevelDb, string? Phrase, string? Voice);

internal sealed record CorpusFile(string Name, string Background, List<PlacedEvent> Events);

internal sealed record Manifest(int Seed, double SecondsPerFile, List<CorpusFile> Files);

/// <summary>
/// Builds the known-truth set: backgrounds a recorder would capture, speech placed at known times and loudness,
/// non-speech sounds that fool detectors, and files of noise alone.
/// </summary>
/// <remarks>
/// <para><b>Level is signal-to-noise, measured where it matters.</b> <see cref="PlacedEvent.LevelDb"/> is the placed
/// sound's active RMS against the background's RMS over the same stretch, both in the voice band (300–3400 Hz). Full
/// band flattered the rumble backgrounds, whose energy sits below any voice. 0 dB means as loud as the hiss where a
/// voice lives; −12 dB is a voice a person strains to hear.</para>
/// <para><b>Limits worth remembering when reading the report.</b> The voices are the Mac's text-to-speech, which is
/// cleaner and steadier than a person; the backgrounds are synthesised until Ben's real recordings are added with
/// <c>--backgrounds</c>. A detector that passes here has earned a test on real tape, not a verdict.</para>
/// </remarks>
internal static class Corpus
{
    public const double SecondsPerFile = 30;
    public static readonly double[] SpeechLevels = [6, 0, -6, -12, -18];

    private static readonly string[] Phrases =
        ["get out", "help me", "who is there", "leave", "hello", "I am cold", "go away", "behind you", "not alone", "Mary", "yes", "come here"];

    private static readonly string[] Voices = ["Samantha", "Fred", "Albert", "Daniel"];
    private static readonly string[] Backgrounds = ["room", "hiss", "static", "hvac"];

    public static Manifest Generate(string outDir, int seed, string? realBackgroundsDir)
    {
        Directory.CreateDirectory(outDir);
        var rng = new Random(seed);
        var speech = SpeechBank(Path.Combine(outDir, "speech"));
        var real = realBackgroundsDir is null ? [] : Directory.GetFiles(realBackgroundsDir)
            .Where(f => f.EndsWith(".wav", true, null) || f.EndsWith(".mp3", true, null) || f.EndsWith(".m4a", true, null))
            .Select(f => (Name: "real:" + Path.GetFileName(f), Samples: Audio.ReadMono16k(f))).Where(r => r.Samples.Length >= Audio.Rate * SecondsPerFile).ToList();

        var files = new List<CorpusFile>();
        var n = 0;

        // 12 speech files per level; half also carry an impostor elsewhere in the file.
        foreach (var level in SpeechLevels)
            for (var i = 0; i < 12; i++)
            {
                var (bgName, audio) = Background(rng, real);
                var events = new List<PlacedEvent>();
                var (phrase, voice, clip) = speech[rng.Next(speech.Count)];
                var start = 3 + rng.NextDouble() * 10;
                events.Add(Place(audio, clip, start, level, "speech", "tts", phrase, voice));
                if (i % 2 == 0) events.Add(PlaceImpostor(rng, audio, 18 + rng.NextDouble() * 8));
                files.Add(Save(outDir, $"{n++:D3}-speech{level:+0;-0}", bgName, audio, events));
            }

        for (var i = 0; i < 20; i++)
        {
            var (bgName, audio) = Background(rng, real);
            var events = new List<PlacedEvent> { PlaceImpostor(rng, audio, 4 + rng.NextDouble() * 8), PlaceImpostor(rng, audio, 17 + rng.NextDouble() * 8) };
            files.Add(Save(outDir, $"{n++:D3}-impostors", bgName, audio, events));
        }

        for (var i = 0; i < 20; i++)
        {
            var (bgName, audio) = Background(rng, real);
            files.Add(Save(outDir, $"{n++:D3}-control", bgName, audio, []));
        }

        var manifest = new Manifest(seed, SecondsPerFile, files);
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return manifest;
    }

    private static CorpusFile Save(string dir, string name, string bg, float[] audio, List<PlacedEvent> events)
    {
        // Leave headroom rather than clip: a clipped file teaches a detector about distortion we did not intend.
        var peak = audio.Max(Math.Abs);
        if (peak > 0.95f) for (var i = 0; i < audio.Length; i++) audio[i] *= 0.95f / peak;
        Audio.WriteMono16k(Path.Combine(dir, name + ".wav"), audio);
        return new CorpusFile(name, bg, events);
    }

    private static PlacedEvent Place(float[] audio, float[] clip, double start, double levelDb, string kind, string subtype, string? phrase, string? voice)
    {
        var at = (int)(start * Audio.Rate);
        var len = Math.Min(clip.Length, audio.Length - at);
        var bgRms = Audio.Rms(Audio.VoiceBand(audio[at..(at + len)]));
        var gain = (float)(bgRms * Math.Pow(10, levelDb / 20) / Math.Max(Audio.ActiveRms(Audio.VoiceBand(clip)), 1e-9));
        for (var i = 0; i < len; i++) audio[at + i] += clip[i] * gain;
        return new PlacedEvent(kind, subtype, start, start + (double)len / Audio.Rate, levelDb, phrase, voice);
    }

    private static PlacedEvent PlaceImpostor(Random rng, float[] audio, double start)
    {
        var (subtype, clip) = Synth.Impostor(rng);
        var level = rng.Next(2) == 0 ? 6.0 : 15.0;
        return Place(audio, clip, start, level, "impostor", subtype, null, null);
    }

    private static (string, float[]) Background(Random rng, List<(string Name, float[] Samples)> real)
    {
        if (real.Count > 0 && rng.Next(2) == 0)
        {
            var (name, s) = real[rng.Next(real.Count)];
            var len = (int)(Audio.Rate * SecondsPerFile);
            var from = rng.Next(s.Length - len + 1);
            return (name, s[from..(from + len)]);
        }
        var kind = Backgrounds[rng.Next(Backgrounds.Length)];
        return (kind, Synth.Background(kind, rng, SecondsPerFile));
    }

    /// <summary>Every phrase in every voice, rendered once by macOS <c>say</c> at 16 kHz and cached.</summary>
    private static List<(string Phrase, string Voice, float[] Clip)> SpeechBank(string dir)
    {
        Directory.CreateDirectory(dir);
        var bank = new List<(string, string, float[])>();
        foreach (var voice in Voices)
            foreach (var phrase in Phrases)
            {
                var path = Path.Combine(dir, $"{voice}-{phrase.Replace(' ', '_')}.wav");
                if (!File.Exists(path))
                {
                    var psi = new ProcessStartInfo("say", ["-v", voice, "-o", path, "--file-format=WAVE", "--data-format=LEI16@16000", phrase]) { RedirectStandardError = true };
                    using var p = Process.Start(psi)!;
                    p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException($"say failed for {voice}: {p.StandardError.ReadToEnd()}");
                }
                bank.Add((phrase, voice, Audio.ReadMono16k(path)));
            }
        return bank;
    }
}
