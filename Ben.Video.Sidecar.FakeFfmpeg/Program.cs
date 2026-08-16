using System.Text.Json;

var mode = Environment.GetEnvironmentVariable("FAKE_FFMPEG_MODE") ?? "ok";
var outDir = Environment.GetEnvironmentVariable("FAKE_FFMPEG_OUT");

// `ffmpeg -version` — answered first and unconditionally, matching the real binary, and used by
// FfmpegLocator/FfmpegRunner's health-check probe regardless of FAKE_FFMPEG_MODE.
if (args.Contains("-version"))
{
    Console.WriteLine("ffmpeg version FAKE-1.0 Copyright (c) 2000-2026 the FFmpeg developers (fake)");
    return 0;
}

if (!string.IsNullOrEmpty(outDir))
{
    Directory.CreateDirectory(outDir);
    var record = new { pid = Environment.ProcessId, args, mode, cwd = Directory.GetCurrentDirectory() };
    var path = Path.Combine(outDir, $"{Environment.ProcessId}.json");
    File.WriteAllText(path, JsonSerializer.Serialize(record));
}

if (mode == "hang")
{
    // Never exits on its own — used to prove the sidecar's per-job timeout actually kills the
    // process tree rather than leaving an orphan.
    await Task.Delay(Timeout.Infinite);
    return 0;
}

if (mode == "fail")
{
    Console.Error.WriteLine("fake ffmpeg: simulated encode failure");
    return 1;
}

// Item #70 phase 159 — ffprobe invocation. The sidecar points both FfmpegTool.Ffmpeg and
// FfmpegTool.Ffprobe at this same fake binary in tests, so the tool is identified from argv
// rather than from the executable name: only the probe path passes `-print_format`.
//
// The payload is deliberately shaped like real ffprobe output (durations as STRINGS, dimensions
// as NUMBERS, an audio stream alongside the video one) so FfprobeOutputParser's mixed-type
// handling is exercised against something realistic rather than a convenient shape.
if (args.Contains("-print_format"))
{
    var probeJson = Environment.GetEnvironmentVariable("FAKE_FFPROBE_JSON");
    if (string.IsNullOrEmpty(probeJson))
    {
        probeJson = """
        {
          "streams": [
            { "codec_type": "video", "duration": "13.80", "width": 640, "height": 360 },
            { "codec_type": "audio", "duration": "13.85" }
          ],
          "format": { "duration": "13.85" }
        }
        """;
    }
    Console.WriteLine(probeJson);
    return 0;
}

// mode == "ok": emit a couple of -progress-shaped lines (the real subset FfmpegRunner/
// ProgressParser care about), then materialize the output file so downstream code that reads it
// back finds something there.
Console.WriteLine("frame=1 fps=0.0 q=0.0 size=0kB time=00:00:00.50 bitrate=0.0kbits/s speed=1.0x");
await Task.Delay(20);
Console.WriteLine("frame=2 fps=30.0 q=28.0 size=100kB time=00:00:01.00 bitrate=800.0kbits/s speed=1.0x");
Console.WriteLine("progress=end");

// Item #70 phase 159 — materialize EVERY output file, not just the last one. A thumbnail job is a
// single exec with N outputs (one -ss/-frames:v/-vf group per frame), so the old
// "last non-flag arg" rule would have created only the final frame and made a working strip look
// like a broken one. Outputs are identified by extension, which is what actually distinguishes an
// output path from an input path or a flag value in these argv shapes.
var outputPaths = args
    .Where(a => !a.StartsWith('-') &&
                (a.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                 a.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)))
    .ToList();

// The input file is passed with -i and shares the .mp4 extension — never overwrite it.
var inputIndex = Array.IndexOf(args, "-i");
if (inputIndex >= 0 && inputIndex + 1 < args.Length) outputPaths.Remove(args[inputIndex + 1]);

// FAKE_FFMPEG_SKIP_OUTPUTS lets a test prove the "ffmpeg exited 0 but produced nothing" branch,
// which is otherwise unreachable through a fake that always writes its outputs.
var skip = Environment.GetEnvironmentVariable("FAKE_FFMPEG_SKIP_OUTPUTS");
var skipCount = int.TryParse(skip, out var n) ? n : 0;

foreach (var outputPath in outputPaths.Skip(skipCount))
{
    try { await File.WriteAllBytesAsync(outputPath, "fake-encoded-bytes"u8.ToArray()); }
    catch { /* tests that don't care about the output file just skip checking it */ }
}

return 0;
