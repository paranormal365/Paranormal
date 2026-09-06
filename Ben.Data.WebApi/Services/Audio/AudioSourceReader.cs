using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// A recording this server will not decode in one piece.
/// </summary>
/// <remarks>
/// Derives from <see cref="NotSupportedException"/> so the endpoints that already answer 400 for
/// an undecodable file answer 400 for an oversized one too, with this message rather than a 500
/// from an allocation that could never have succeeded.
/// </remarks>
public sealed class AudioTooLargeException(string message) : NotSupportedException(message);

/// <summary>
/// Opens stored audio for reading, in the one place every server-side audio feature goes through.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> NAudio's <see cref="Mp3FileReader"/> defaults to the ACM codec,
/// which is <c>Msacm32.dll</c> — a Windows system library. On macOS and Linux every MP3 read threw
/// <see cref="DllNotFoundException"/> at construction, so <c>AudioEditor</c>, <c>AudioClipper</c>,
/// <c>AudioMixer</c> and EVP detection all failed on any MP3 with a 500. WAV worked, which is why
/// this went unnoticed.</para>
///
/// <para>The fix is NLayer's fully-managed MP3 frame decompressor, which behaves identically on
/// every platform. Passing it explicitly also removes the hidden dependency on which OS the API
/// happens to be deployed to.</para>
///
/// <para><b>And why the reading lives here too.</b> Every caller used to grow a
/// <c>List&lt;float&gt;</c> a chunk at a time and then call <c>ToArray</c> on it, which holds the
/// list's doubling buffer and a full copy at once — three times the decoded audio, before the
/// operation had done anything. Measured on a 90-minute stereo recording, one Normalize peaked at
/// 8.6 GB and one EVP scan at 5.1 GB, and neither gave it back (2026-09-06 audio walk, findings 1
/// and 1b). Reading through here allocates the buffer once, from the length the format already
/// knows.</para>
/// </remarks>
internal static class AudioSourceReader
{
    /// <summary>
    /// The longest recording a destructive edit will decode.
    /// </summary>
    /// <remarks>
    /// <para>An edit holds the whole decoded recording, the whole result, and the bytes on their
    /// way to storage. Half an hour of stereo at 44.1 kHz is about 1.3 GB of that, which one API
    /// can survive; ninety minutes is not, and answering "too long" is better than dying with an
    /// out-of-memory error that takes every other request with it.</para>
    ///
    /// <para>Settable so a deployment with more room, or a test, can say otherwise. The EVP scan
    /// deliberately has no such limit: it reads through <see cref="ReadMonoAt"/>, which never holds
    /// the recording at its original rate.</para>
    /// </remarks>
    public static TimeSpan MaximumEditDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The rate the EVP detector reads at.
    /// </summary>
    /// <remarks>
    /// The detector band-passes 300–3400 Hz and looks at nothing above it, so decoding at 44.1 kHz
    /// stereo and averaging afterwards costs five and a half times the memory for no information.
    /// 16 kHz leaves the whole speech band well inside Nyquist.
    /// </remarks>
    public const int DetectionSampleRate = 16_000;

    /// <summary>
    /// Opens <paramref name="stream"/> as a <see cref="WaveStream"/>, choosing a reader from
    /// <paramref name="contentType"/>. The caller owns the returned stream.
    /// </summary>
    /// <exception cref="NotSupportedException">The content type isn't a format we decode.</exception>
    public static WaveStream Open(Stream stream, string? contentType)
    {
        if (stream.CanSeek) stream.Position = 0;

        var type = (contentType ?? string.Empty).ToLowerInvariant();

        // Mp3FileReaderBase, not Mp3FileReader: only the base exposes the decompressor-builder
        // overload, and the derived type's constructors always pick the ACM (Windows) decoder.
        if (type.Contains("mpeg") || type.Contains("mp3"))
            return new Mp3FileReaderBase(stream, wf => new Mp3FrameDecompressor(wf));

        if (type.Contains("wav") || type.Contains("wave"))
            return new WaveFileReader(stream);

        // Anything else is rejected rather than guessed at, so callers keep turning it into a
        // 400 with a message the user can act on instead of a decoder-internal failure.
        throw new NotSupportedException(
            $"Audio is supported for WAV and MP3. Received content-type: '{contentType}'.");
    }

    /// <summary>
    /// Decodes the whole recording to interleaved samples in [-1, 1].
    /// </summary>
    /// <exception cref="AudioTooLargeException">
    /// The recording is longer than <see cref="MaximumEditDuration"/>. Checked from the header
    /// before a byte is decoded, so an impossible request costs nothing.
    /// </exception>
    /// <remarks>
    /// One buffer, sized from the stream's own length. WAV knows exactly how many samples it holds,
    /// so nothing is copied at all; MP3's length is an estimate from its bitrate, so the buffer is
    /// grown or trimmed once if the estimate was wrong.
    /// </remarks>
    public static (float[] Samples, WaveFormat Format) ReadAll(Stream stream, string? contentType)
    {
        using var waveStream = Open(stream, contentType);

        if (waveStream.TotalTime > MaximumEditDuration)
            throw new AudioTooLargeException(
                $"That recording is {Describe(waveStream.TotalTime)} long, and edits are limited to "
                + $"{Describe(MaximumEditDuration)}. Cut the part you need out of it first — a clip "
                + "saved from a region can be edited like any other file.");

        var provider = waveStream.ToSampleProvider();
        return (ReadInto(provider, EstimateSamples(waveStream, provider.WaveFormat)), provider.WaveFormat);
    }

    /// <summary>
    /// Decodes the whole recording as mono at <paramref name="targetSampleRate"/>.
    /// </summary>
    /// <remarks>
    /// <para>No length limit, because there is no longer a reason for one: an hour and a half of
    /// stereo that cost 1.9 GB to hold at its own rate costs about 350 MB here, and it is held
    /// once. The mixdown and the rate change happen as the stream is read, so the original rate is
    /// never materialised.</para>
    ///
    /// <para>What this is for is the EVP scan, whose whole job is long recordings — the one thing
    /// the ceiling above must not refuse.</para>
    /// </remarks>
    public static (float[] Mono, int SampleRate) ReadMonoAt(
        Stream stream, string? contentType, int targetSampleRate = DetectionSampleRate)
    {
        using var waveStream = Open(stream, contentType);

        ISampleProvider provider = waveStream.ToSampleProvider();

        if (provider.WaveFormat.Channels > 1)
            provider = new MonoMixdown(provider);

        if (provider.WaveFormat.SampleRate != targetSampleRate)
            provider = new WdlResamplingSampleProvider(provider, targetSampleRate);

        var estimate = (long)(waveStream.TotalTime.TotalSeconds * targetSampleRate) + targetSampleRate;

        return (ReadInto(provider, estimate), targetSampleRate);
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// How many samples are asked for at a time.
    /// </summary>
    /// <remarks>
    /// Reading is chunked, not because the destination needs it, but because every provider in
    /// front of it sizes its own scratch buffer from what it is asked for. Requesting the whole
    /// remaining array in one call made the mono mixdown allocate an interleaved copy of the
    /// entire recording and the resampler do the same — 5.2 GB of allocation to produce a 329 MB
    /// result, which is most of what this method exists to avoid. A quarter of a million samples
    /// is a few seconds of audio and a megabyte of scratch.
    /// </remarks>
    private const int ReadChunkSamples = 1 << 18;

    /// <summary>
    /// Drains <paramref name="provider"/> into one array, starting at <paramref name="estimate"/>.
    /// </summary>
    private static float[] ReadInto(ISampleProvider provider, long estimate)
    {
        // A cap the arithmetic cannot exceed: .NET refuses a single array over about 2.1 billion
        // elements, and reaching it means the estimate was nonsense rather than the file being big.
        const long ceiling = 0x7FEFFFFF;

        var capacity = (int)Math.Clamp(estimate <= 0 ? 1 << 20 : estimate, 1 << 16, ceiling);
        var buffer   = new float[capacity];
        var count    = 0;

        while (true)
        {
            if (count == buffer.Length)
            {
                if (buffer.Length >= ceiling)
                    throw new AudioTooLargeException(
                        "That recording is too long for this server to decode in one piece.");

                // Half again, not double: the estimate is usually right, and overshooting a
                // gigabyte-scale buffer is how this ran out of memory in the first place.
                Array.Resize(ref buffer, (int)Math.Min(ceiling, buffer.Length + buffer.Length / 2L));
            }

            var read = provider.Read(buffer, count, Math.Min(ReadChunkSamples, buffer.Length - count));
            if (read == 0) break;
            count += read;
        }

        // Only when the estimate was wrong. For WAV it never is.
        if (count != buffer.Length) Array.Resize(ref buffer, count);

        return buffer;
    }

    private static long EstimateSamples(WaveStream waveStream, WaveFormat format)
    {
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);

        // WaveStream.Length is exact for WAV and an estimate for MP3; either way it is far closer
        // than starting from nothing.
        var fromLength = waveStream.Length > 0 ? waveStream.Length / bytesPerSample : 0;

        var fromTime = (long)(waveStream.TotalTime.TotalSeconds * format.SampleRate * Math.Max(1, format.Channels));

        return Math.Max(fromLength, fromTime);
    }

    private static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{span.TotalMinutes:0.#} minutes"
            : $"{span.TotalSeconds:0} seconds";

    /// <summary>
    /// Averages every channel into one, as the stream is read.
    /// </summary>
    /// <remarks>
    /// NAudio ships a stereo-to-mono provider, but it handles exactly two channels and a field
    /// recorder can hand back four. This averages whatever it is given.
    /// </remarks>
    private sealed class MonoMixdown(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = Math.Max(1, source.WaveFormat.Channels);
        private float[] _interleaved = [];

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var wanted = count * _channels;
            if (_interleaved.Length < wanted) _interleaved = new float[wanted];

            var read = source.Read(_interleaved, 0, wanted);
            var frames = read / _channels;

            for (var frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                for (var channel = 0; channel < _channels; channel++)
                    sum += _interleaved[frame * _channels + channel];

                buffer[offset + frame] = (float)(sum / _channels);
            }

            return frames;
        }
    }
}
