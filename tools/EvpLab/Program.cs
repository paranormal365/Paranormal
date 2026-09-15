using EvpLab;

// Item 242, phase 1 — the measuring bench. See ProjectNotes/FeatureHistory/README-evp-ai-242.md.
//
//   dotnet run -c Release -- generate [--out out/corpus] [--seed 242] [--backgrounds <folder of real recordings>]
//   dotnet run -c Release -- score    [--corpus out/corpus] [--whisper models/ggml-small.bin] [--threads 8] [--noise-windows 8]

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
        var text = Bench.Run(corpus, Arg("--whisper", "models/ggml-small.bin"), int.Parse(Arg("--threads", "8")), int.Parse(Arg("--noise-windows", "8")));
        var path = Path.Combine(corpus, $"report-{DateTime.Now:yyyyMMdd-HHmm}.md");
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        Console.WriteLine($"Report: {Path.GetFullPath(path)}");
        return 0;
    }
    default:
        Console.Error.WriteLine("usage: generate | score  (see Program.cs)");
        return 2;
}
