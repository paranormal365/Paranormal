using EvpLab;

// Item 242, phase 1 — the measuring bench. See ProjectNotes/FeatureHistory/README-evp-ai-242.md.
//
//   dotnet run -c Release -- generate [--out out/corpus] [--seed 242] [--backgrounds <folder of real recordings>]
//   dotnet run -c Release -- score    [--corpus out/corpus] [--whisper models/ggml-small.bin|none] [--vad models/silero_vad.onnx|none] [--threads 8] [--noise-windows 8]

string Arg(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

switch (args.FirstOrDefault())
{
    case "generate":
    {
        var outDir = Arg("--out", "out/corpus");
        var backgrounds = Arg("--backgrounds", "");
        var manifest = Corpus.Generate(outDir, int.Parse(Arg("--seed", "242")), backgrounds.Length == 0 ? null : backgrounds);
        Console.WriteLine($"Wrote {manifest.Files.Count} files and manifest.json to {Path.GetFullPath(outDir)}");
        return 0;
    }
    case "score":
    {
        var corpus = Arg("--corpus", "out/corpus");
        var text = Bench.Run(corpus, Arg("--whisper", "models/ggml-small.bin"), Arg("--vad", "models/silero_vad.onnx"), int.Parse(Arg("--threads", "8")), int.Parse(Arg("--noise-windows", "8")));
        var path = Path.Combine(corpus, $"report-{DateTime.Now:yyyyMMdd-HHmm}.md");
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        Console.WriteLine($"Report: {Path.GetFullPath(path)}");
        return 0;
    }
    case "probe":   // probe <wav> [model]: the VAD's peak probability on a file as recorded and peak-normalised
    {
        using var vad = new SileroVad(args.Length > 2 ? args[2] : "models/silero_vad.onnx");
        var samples = Audio.ReadMono16k(args[1]);
        var peak = samples.Max(Math.Abs);
        var normalised = samples.Select(v => v * 0.89f / Math.Max(peak, 1e-9f)).ToArray();
        Console.WriteLine($"{Path.GetFileName(args[1])}: peak {Audio.Db(peak):0.0} dBFS; max p as recorded {vad.Probabilities(samples).DefaultIfEmpty().Max():0.00}, " +
                          $"peak-normalised {vad.Probabilities(normalised).DefaultIfEmpty().Max():0.00}");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: generate | score  (see Program.cs)");
        return 2;
}
