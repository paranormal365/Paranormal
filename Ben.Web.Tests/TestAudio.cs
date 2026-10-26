using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Audio;
using Microsoft.Extensions.Options;

namespace Ben.Web.Tests;

/// <summary>
/// The audio decoders a controller fixture needs, built once here.
/// </summary>
public static class TestAudio
{
    /// <summary>
    /// The decoder production registers, with ffmpeg NOT configured — fixtures must never shell
    /// out, and MP3 and WAV decode exactly as they always have. The ffmpeg path is covered directly
    /// in <c>FfmpegAudioDecoderTests</c>, against the binary on this machine when there is one.
    /// </summary>
    public static IAudioDecoder Decoder()
        => new CompositeAudioDecoder(new NAudioDecoder(), Ffmpeg(null));

    /// <summary>
    /// A stand-in for ffmpeg that reads whatever it is handed as WAV — so a fixture can seed a
    /// WAV labelled <c>audio/mp4</c> and prove the controller went through the decoder, which is
    /// the only way such a file could have been read.
    /// </summary>
    public static IAudioDecoder ReadingEverythingAsWav() => new EverythingIsWav();

    private sealed class EverythingIsWav : IAudioDecoder
    {
        private readonly NAudioDecoder _naudio = new();
        public bool IsAvailable => true;
        public bool CanDecode(string? contentType, string? fileName) => true;
        public IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(Stream s, string? t, string? n, CancellationToken ct)
            => _naudio.DecodeMono16kAsync(s, "audio/wav", n, ct);
        public Task<float[]> ReadMono16kAsync(Stream s, string? t, string? n, CancellationToken ct)
            => _naudio.ReadMono16kAsync(s, "audio/wav", n, ct);
        public Task<byte[]> DecodeRangeToWavAsync(Stream s, string? t, string? n, double a, double b, CancellationToken ct)
            => _naudio.DecodeRangeToWavAsync(s, "audio/wav", n, a, b, ct);
        public Task<double?> DurationSecondsAsync(Stream s, string? t, string? n, CancellationToken ct)
            => _naudio.DurationSecondsAsync(s, "audio/wav", n, ct);
    }

    /// <summary>
    /// The encoder production registers, with ffmpeg NOT configured: every clip and edit stays WAV,
    /// as it was before phase 4b, so a fixture never shells out.
    /// </summary>
    public static IAudioEncoder Encoder() => Ffmpeg(null);

    /// <summary>
    /// A stand-in for ffmpeg's FLAC encoder that wraps the WAV it is given behind a "fLaC" marker,
    /// so a test can tell the controller asked for FLAC and stored what came back. The real
    /// round trip is <c>FlacSaveTests</c>, against ffmpeg on this machine.
    /// </summary>
    public static IAudioEncoder MarkingFlac() => new MarkingFlacEncoder();

    private sealed class MarkingFlacEncoder : IAudioEncoder
    {
        public bool CanEncodeFlac => true;
        public Task<byte[]?> TryWavToFlacAsync(byte[] wav, CancellationToken ct)
            => Task.FromResult<byte[]?>([.. "fLaC"u8, .. wav.AsSpan(0, wav.Length / 2)]);
    }

    /// <summary>An ffmpeg decoder pointed at <paramref name="path"/>.</summary>
    public static FfmpegAudioDecoder Ffmpeg(string? path, int decodeTimeoutSeconds = 600)
        => new(Options.Create(new MediaToolOptions { FfmpegPath = path, DecodeTimeoutSeconds = decodeTimeoutSeconds }));

    /// <summary>
    /// ffmpeg on this machine — <c>MediaTools__FfmpegPath</c> when set, else the usual install
    /// places, else the PATH — or null when there is none.
    /// </summary>
    public static string? FfmpegOnThisMachine() => Find("ffmpeg", "MediaTools__FfmpegPath");

    /// <summary>ffprobe beside it, for checking a fixture before trusting a decode of it.</summary>
    public static string? FfprobeOnThisMachine()
    {
        if (FfmpegOnThisMachine() is { } ffmpeg)
        {
            var beside = Path.Combine(Path.GetDirectoryName(ffmpeg)!,
                                      OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
            if (File.Exists(beside)) return beside;
        }
        return Find("ffprobe", null);
    }

    /// <summary>A fixture copied beside the test assembly.</summary>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string? Find(string tool, string? envVar)
    {
        if (envVar is not null && Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } configured
            && File.Exists(configured))
            return configured;

        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        var candidates = new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return candidates
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Select(dir => Path.Combine(dir, exe))
            .FirstOrDefault(File.Exists);
    }
}
