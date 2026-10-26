using System.Buffers.Binary;
using System.Diagnostics;
using Ben.Data.WebApi.Services.Audio;
using NAudio.Wave;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// <see cref="FfmpegAudioDecoder"/> against a real phone recording and the ffmpeg on this machine.
/// </summary>
/// <remarks>
/// <para>Skipped, with the reason, when this machine has no ffmpeg — a test host without it is
/// honest, and production's own check is the dashboard's "Audio decoding" card. Run with ffmpeg
/// installed (Homebrew puts it at /opt/homebrew/bin) or <c>MediaTools__FfmpegPath</c> set.</para>
///
/// <para>The fixture is checked with ffprobe before any decode is trusted: a test that "decodes"
/// a file that was never the recording it claims to be proves nothing.</para>
/// </remarks>
public class FfmpegAudioDecoderTests
{
    private const string Clip = "device-clip.m4a";

    // ── The fixture itself (no ffmpeg needed) ─────────────────────────────────

    /// <summary>
    /// The layout this whole decoder exists for: <c>moov</c> after <c>mdat</c>, which ffmpeg cannot
    /// read from a pipe. If the fixture is ever re-cut with <c>-movflags +faststart</c>, every test
    /// below would pass against a decoder that reads <c>pipe:0</c> and breaks on the phone's files.
    /// </summary>
    [Fact]
    public void DeviceClip_KeepsTheMovieIndexAfterTheAudio()
    {
        var boxes = TopLevelBoxes(File.ReadAllBytes(TestAudio.Fixture(Clip)));

        Assert.Contains("moov", boxes);
        Assert.Contains("mdat", boxes);
        Assert.True(boxes.IndexOf("moov") > boxes.IndexOf("mdat"),
            $"device-clip.m4a must keep moov after mdat; its boxes are {string.Join(", ", boxes)}.");
    }

    [SkippableFact]
    public void DeviceClip_IsThreeSecondsOfAac()
    {
        var ffprobe = TestAudio.FfprobeOnThisMachine();
        Skip.If(ffprobe is null, "No ffprobe on this machine, so the fixture cannot be checked. Install ffmpeg.");

        var (codec, seconds) = Probe(ffprobe!, TestAudio.Fixture(Clip));

        Assert.Equal("aac", codec);
        Assert.InRange(seconds, 2.8, 3.2);
    }

    // ── Decoding ──────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ReadMono16k_DecodesTheWholeClipAtSixteenKilohertz()
    {
        var decoder = Decoder();

        await using var source = File.OpenRead(TestAudio.Fixture(Clip));
        var mono = await decoder.ReadMono16kAsync(source, "audio/mp4", Clip, default);

        Assert.InRange(mono.Length / (double)AudioSourceReader.DetectionSampleRate, 2.8, 3.2);
        Assert.Contains(mono, s => Math.Abs(s) > 0.001f);    // a recording, not silence
        Assert.All(mono, s => Assert.InRange(s, -1f, 1f));
    }

    /// <summary>
    /// The phone's file, handed over as a stream that cannot seek — what storage gives back from a
    /// remote store. (Too short to fail through a pipe on its own; the next test is the one that
    /// would.)
    /// </summary>
    [SkippableFact]
    public async Task ReadMono16k_ReadsAStreamThatCannotSeek()
    {
        var decoder = Decoder();

        await using var source = new ForwardOnly(File.ReadAllBytes(TestAudio.Fixture(Clip)));
        var mono = await decoder.ReadMono16kAsync(source, "audio/mp4", Clip, default);

        Assert.InRange(mono.Length / (double)AudioSourceReader.DetectionSampleRate, 2.8, 3.2);
    }

    /// <summary>
    /// The case that actually tells a pipe from a file. Three seconds is small enough to sit in
    /// ffmpeg's read buffer, so even <c>pipe:0</c> reads the fixture (checked 10/26/2026, ffmpeg
    /// 9.0.2). Thirty-three seconds with the index at the end is not: from a pipe ffmpeg answers
    /// "partial file" and produces nothing — which is what a real session recording would get.
    /// </summary>
    [SkippableFact]
    public async Task ReadMono16k_ReadsALongRecordingWhoseIndexIsAtTheEnd()
    {
        var decoder = Decoder();
        var longer  = Path.Combine(Path.GetTempPath(), $"ben-test-long-{Guid.NewGuid():N}.m4a");
        try
        {
            // -c copy keeps moov last, exactly as the fixture was cut.
            Run(TestAudio.FfmpegOnThisMachine()!, "-nostdin", "-hide_banner", "-loglevel", "error",
                "-stream_loop", "10", "-i", TestAudio.Fixture(Clip), "-c", "copy", longer);
            var bytes = File.ReadAllBytes(longer);
            var boxes = TopLevelBoxes(bytes);
            Assert.True(boxes.IndexOf("moov") > boxes.IndexOf("mdat"), string.Join(", ", boxes));

            await using var source = new ForwardOnly(bytes);
            var mono = await decoder.ReadMono16kAsync(source, "audio/mp4", "long.m4a", default);

            Assert.InRange(mono.Length / (double)AudioSourceReader.DetectionSampleRate, 30, 36);
        }
        finally
        {
            File.Delete(longer);
        }
    }

    [SkippableFact]
    public async Task DecodeMono16k_InChunks_GivesTheSameSamplesAsReadingItWhole()
    {
        var decoder = Decoder();

        float[] whole;
        await using (var source = File.OpenRead(TestAudio.Fixture(Clip)))
            whole = await decoder.ReadMono16kAsync(source, "audio/mp4", Clip, default);

        var pieces = new List<float>();
        await using (var source = File.OpenRead(TestAudio.Fixture(Clip)))
            await foreach (var chunk in decoder.DecodeMono16kAsync(source, "audio/mp4", Clip, default))
            {
                Assert.InRange(chunk.Length, 1, IAudioDecoder.ChunkSamples);
                pieces.AddRange(chunk.ToArray());
            }

        Assert.Equal(whole, pieces.ToArray());
    }

    [SkippableFact]
    public async Task DecodeRangeToWav_IsAWavOfTheRangeAskedFor()
    {
        var decoder = Decoder();

        await using var source = File.OpenRead(TestAudio.Fixture(Clip));
        var wav = await decoder.DecodeRangeToWavAsync(source, "audio/mp4", Clip, 0.5, 1.5, default);

        // NAudio refuses the 0xFFFFFFFF length a WAV written to a pipe carries, so this reading at
        // all is the check that the range went through a file.
        using var reader = new WaveFileReader(new MemoryStream(wav));
        Assert.InRange(reader.TotalTime.TotalSeconds, 0.9, 1.1);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
    }

    [SkippableFact]
    public async Task DurationSeconds_ComesFromTheHeader()
    {
        var decoder = Decoder();

        await using var source = File.OpenRead(TestAudio.Fixture(Clip));
        var seconds = await decoder.DurationSecondsAsync(source, "audio/mp4", Clip, default);

        Assert.NotNull(seconds);
        Assert.InRange(seconds!.Value, 2.8, 3.2);
    }

    [SkippableFact]
    public async Task ABrokenRecording_FailsWithFfmpegsReason()
    {
        var decoder = Decoder();

        await using var source = new MemoryStream(Enumerable.Repeat((byte)7, 4096).ToArray());
        var ex = await Assert.ThrowsAsync<AudioDecodeFailedException>(
            () => decoder.ReadMono16kAsync(source, "audio/mp4", "broken.m4a", default));

        Assert.StartsWith("ffmpeg could not read it:", ex.Message);
        Assert.True(AudioSourceReader.IsUndecodable(ex), "every audio endpoint must answer this with a 400");
    }

    [SkippableFact]
    public async Task TheScratchCopy_IsDeletedAfterwards()
    {
        var decoder = Decoder();
        var before  = ScratchFiles();

        await using (var source = File.OpenRead(TestAudio.Fixture(Clip)))
            await decoder.ReadMono16kAsync(source, "audio/mp4", Clip, default);
        await using (var source = File.OpenRead(TestAudio.Fixture(Clip)))
            await decoder.DecodeRangeToWavAsync(source, "audio/mp4", Clip, 0, 1, default);
        await using (var source = new MemoryStream([1, 2, 3]))
            await Assert.ThrowsAsync<AudioDecodeFailedException>(
                () => decoder.ReadMono16kAsync(source, "audio/mp4", "broken.m4a", default));

        Assert.Empty(ScratchFiles().Except(before));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FfmpegAudioDecoder Decoder()
    {
        var ffmpeg = TestAudio.FfmpegOnThisMachine();
        Skip.If(ffmpeg is null,
            "No ffmpeg on this machine, so the .m4a decoder cannot be exercised. Install ffmpeg or set MediaTools__FfmpegPath.");
        return TestAudio.Ffmpeg(ffmpeg);
    }

    private static string[] ScratchFiles()
        => Directory.GetFiles(Path.GetTempPath(), "ben-evp-*");

    private static List<string> TopLevelBoxes(byte[] bytes)
    {
        var boxes = new List<string>();
        long at = 0;
        while (at + 8 <= bytes.Length)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan((int)at, 4));
            boxes.Add(System.Text.Encoding.ASCII.GetString(bytes, (int)at + 4, 4));
            if (size == 1) size = (long)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan((int)at + 8, 8));
            if (size < 8) break;
            at += size;
        }
        return boxes;
    }

    private static void Run(string tool, params string[] args)
    {
        var psi = new ProcessStartInfo(tool) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{Path.GetFileName(tool)} failed: {stderr}");
    }

    private static (string Codec, double Seconds) Probe(string ffprobe, string path)
    {
        var psi = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "a:0", "-show_entries",
                                    "stream=codec_name:format=duration", "-of", "default=nw=1", path })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var fields = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        return (fields.GetValueOrDefault("codec_name", ""),
                double.Parse(fields.GetValueOrDefault("duration", "0"), System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>A stream that reads forward only, like a download.</summary>
    private sealed class ForwardOnly(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}

/// <summary>
/// <see cref="CompositeAudioDecoder"/> on a host with no ffmpeg — the state of every server until
/// <c>MediaTools:FfmpegPath</c> is set.
/// </summary>
public class CompositeAudioDecoderTests
{
    private static IAudioDecoder WithoutFfmpeg() => TestAudio.Decoder();

    [Fact]
    public async Task AnM4a_IsRefusedWithASentenceAboutTheServer()
    {
        await using var source = new MemoryStream(File.ReadAllBytes(TestAudio.Fixture("device-clip.m4a")));

        var ex = await Assert.ThrowsAsync<AudioDecoderUnavailableException>(
            () => WithoutFfmpeg().ReadMono16kAsync(source, "audio/mp4", "device-clip.m4a", default));

        Assert.Equal("This server cannot read .m4a yet.", ex.Message);
    }

    [Fact]
    public async Task AnotherKindOfAudio_IsNotToldItIsAnM4a()
    {
        await using var source = new MemoryStream(new byte[64]);

        var ex = await Assert.ThrowsAsync<AudioDecoderUnavailableException>(
            () => WithoutFfmpeg().DecodeRangeToWavAsync(source, "audio/ogg", "x.ogg", 0, 1, default));

        Assert.DoesNotContain(".m4a", ex.Message);
        Assert.Contains("WAV", ex.Message);
    }

    [Fact]
    public async Task Mp3_StillDecodesWithoutFfmpeg()
    {
        await using var source = File.OpenRead(TestAudio.Fixture("test-audio.mp3"));

        var mono = await WithoutFfmpeg().ReadMono16kAsync(source, "audio/mpeg", "test-audio.mp3", default);

        Assert.NotEmpty(mono);
    }

    [Fact]
    public void CanDecode_SaysNoToM4a_AndYesToWav_WithoutFfmpeg()
    {
        Assert.False(WithoutFfmpeg().CanDecode("audio/mp4", "a.m4a"));
        Assert.True(WithoutFfmpeg().CanDecode("audio/wav", "a.wav"));
    }

    [Fact]
    public async Task SomethingThatIsNotAudio_IsRefusedAsNotAudio()
    {
        await using var source = new MemoryStream(new byte[64]);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => WithoutFfmpeg().ReadMono16kAsync(source, "image/png", "x.png", default));

        Assert.IsNotType<AudioDecoderUnavailableException>(ex);
    }
}

/// <summary>
/// Where the API looks for ffmpeg. On shared hosting nobody knows the site's folder on disk, so a
/// relative path is taken from the API's own folder, the way the key files already are.
/// </summary>
public class MediaToolOptionsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ben-site");

    [Fact]
    public void A_relative_path_is_taken_from_the_site_folder()
    {
        var options = new Ben.Data.WebApi.Services.MediaToolOptions
            { FfmpegPath = Path.Combine("App_Data", "Tools", "ffmpeg.exe") };

        options.ResolveAgainst(Root);

        Assert.Equal(Path.Combine(Root, "App_Data", "Tools", "ffmpeg.exe"), options.FfmpegPath);
    }

    [Fact]
    public void An_absolute_path_is_left_as_it_is()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "tools", "ffmpeg");
        var options  = new Ben.Data.WebApi.Services.MediaToolOptions { FfmpegPath = absolute };

        options.ResolveAgainst(Root);

        Assert.Equal(absolute, options.FfmpegPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_path_stays_no_path(string? path)
    {
        var options = new Ben.Data.WebApi.Services.MediaToolOptions { FfmpegPath = path };

        options.ResolveAgainst(Root);

        Assert.Equal(path, options.FfmpegPath);
    }
}
