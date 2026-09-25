using System.Diagnostics;
using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Ben.Video.Sidecar.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Runs a real export argv against a real ffmpeg and checks a real file comes out.
/// </summary>
/// <remarks>
/// <para>Every other test here asserts about strings. That is the right way to pin a rule, but it
/// cannot tell you whether ffmpeg accepts the command, and the failure being guarded against -
/// naming an encoder a build does not have, or handing an encoder an option it does not know -
/// only ever shows up when ffmpeg is actually asked. So this asks it.</para>
///
/// <para>Point <c>SIDECAR_TEST_FFMPEG</c> at an ffmpeg binary to run it, most usefully one WITHOUT
/// libx264, which is what a Microsoft Store package would carry:</para>
/// <code>
/// $env:SIDECAR_TEST_FFMPEG = "C:\path\to\lgpl\bin\ffmpeg.exe"
/// dotnet test --filter FullyQualifiedName~RealFfmpegExportTests
/// </code>
/// <para>Without that variable it passes without asking anything, because the binary it needs is
/// not in the repository and a failure here would be reporting on the machine, not the code.</para>
/// </remarks>
public sealed class RealFfmpegExportTests(ITestOutputHelper output)
{
    private static readonly ClipEffectRegistry Registry = DefaultEffectRegistry.CreateDefault();

    private string? Ffmpeg()
    {
        var path = Environment.GetEnvironmentVariable("SIDECAR_TEST_FFMPEG");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            output.WriteLine("Set SIDECAR_TEST_FFMPEG to an ffmpeg binary to run this. Nothing asked.");
            return null;
        }
        return path;
    }

    [Theory]
    [InlineData(ExportVideoCodec.H264)]
    [InlineData(ExportVideoCodec.H265)]
    [InlineData(ExportVideoCodec.Vp9)]
    public void An_export_produces_a_real_video_whatever_the_build_offers(ExportVideoCodec codec)
    {
        var ffmpeg = Ffmpeg();
        if (ffmpeg is null) return;

        var dir = Path.Combine(Path.GetTempPath(), $"sidecar-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var encoders = Probe(ffmpeg);

            string chosen;
            try
            {
                chosen = VideoEncoders.Choose(codec, encoders);
            }
            catch (InvalidOperationException ex)
            {
                // A build that cannot do this format at all is an allowed outcome - see the H.265
                // note in VideoEncoders. What matters is that it is refused before ffmpeg runs, and
                // that the message tells the person what to pick instead.
                output.WriteLine($"{codec} refused: {ex.Message}");
                Assert.Contains("Choose", ex.Message);
                return;
            }
            output.WriteLine($"{codec} -> {chosen}");

            var source = Path.Combine(dir, "source.mp4");
            MakeSource(ffmpeg, source);

            // The very argv the sidecar would build for this export, encoder list and all.
            var spec = ExportSpec(codec);
            var args = ArgvFactory.Build(spec, source, "out.mp4", Registry, encoders);
            output.WriteLine("ffmpeg " + string.Join(' ', args));

            var (exit, stderr) = Run(ffmpeg, args, dir);

            var produced = new FileInfo(Path.Combine(dir, "out.mp4"));
            Assert.True(exit == 0, $"ffmpeg exited {exit}:\n{Tail(stderr)}");
            Assert.True(produced.Exists, "ffmpeg reported success but wrote no file.");

            // A zero-byte file is the failure mode of an encoder that is listed but cannot run -
            // it is what the graphics-card encoders do on a machine without the hardware - and
            // exit code 0 does not rule it out.
            Assert.True(produced.Length > 1024, $"Output is {produced.Length} bytes, which is not a video.");
            output.WriteLine($"{produced.Length:N0} bytes.");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* a temp directory */ }
        }
    }

    private static SegmentRenderSpec ExportSpec(ExportVideoCodec codec) => new(
        Kind: SegmentKind.Video,
        ClipId: Guid.NewGuid(),
        SourceExt: ".mp4",
        Pass: RenderPassKind.Export,
        Duration: 2.0,
        StartTrim: 0.0,
        EndTrim: 2.0,
        Speed: 1.0,
        MuteAudio: false,
        Gain: 1.0,
        OutputWidth: 320,
        OutputHeight: 180,
        Effects: null,
        AppliedEffects: [],
        VolumeAutomation: [],
        ExportQuality: new ExportQualityDto(
            VideoCodec: codec, AudioCodec: ExportAudioCodec.Aac,
            Bitrate: 1000, UseCrf: true, Crf: 23,
            IncludeAudio: true, AudioBitrate: 128,
            Preset: ExportPresetKind.Fast, Fps: 30));

    private static void MakeSource(string ffmpeg, string path)
    {
        var (exit, stderr) = Run(ffmpeg, [
            "-y",
            "-f", "lavfi", "-i", "testsrc=size=320x180:rate=30:duration=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-c:v", "mpeg4", "-c:a", "aac", "-shortest", path,
        ], Path.GetDirectoryName(path)!);

        // mpeg4 is in every build there is, so making the input cannot itself be the thing that fails.
        Assert.True(exit == 0, $"Could not make a test source:\n{Tail(stderr)}");
    }

    private static IReadOnlyCollection<string> Probe(string ffmpeg)
    {
        var locator = new FfmpegLocator(AppContext.BaseDirectory, developmentPathOverride: ffmpeg);
        return new FfmpegEncoders(locator, NullLogger<FfmpegEncoders>.Instance).Names;
    }

    private static (int Exit, string StdErr) Run(string exe, IEnumerable<string> args, string workDir)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit(TimeSpan.FromMinutes(3));
        return (p.ExitCode, stderr.GetAwaiter().GetResult());
    }

    private static string Tail(string s)
    {
        var lines = s.Split('\n');
        return string.Join('\n', lines[Math.Max(0, lines.Length - 12)..]);
    }
}
