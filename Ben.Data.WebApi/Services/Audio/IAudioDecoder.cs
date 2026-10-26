namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// This server has no way to read a recording of this kind — not a problem with the file.
/// </summary>
/// <remarks>
/// Derives from <see cref="NotSupportedException"/> so an endpoint that has not learned about it
/// still answers 400 with this sentence rather than 500. The EVP scan catches it first and answers
/// with a <c>ScanRefusalRecord</c>, because "this server cannot read .m4a" is something an
/// administrator fixes, not something the person asking can.
/// </remarks>
public sealed class AudioDecoderUnavailableException(string message) : NotSupportedException(message);

/// <summary>
/// ffmpeg ran and could not read the recording, or ran out of time doing it.
/// </summary>
/// <remarks>
/// A <see cref="FormatException"/>, so <see cref="AudioSourceReader.IsUndecodable"/> already
/// treats it as "these bytes are not the audio they claim to be" and every audio endpoint answers
/// 400 with ffmpeg's own last lines, the part that says why.
/// </remarks>
public sealed class AudioDecodeFailedException(string message) : FormatException(message);

/// <summary>
/// Turns stored audio into samples, whatever it was recorded as.
/// </summary>
/// <remarks>
/// <para><b>Why there is more than one.</b> NAudio reads MP3 and WAV in-process on every platform
/// (see <see cref="AudioSourceReader"/>), but the phone records AAC in an <c>.m4a</c>, and nothing
/// managed reads that on Windows and macOS alike. ffmpeg does, and production already has it for
/// the feed's video screening. <see cref="CompositeAudioDecoder"/> is the one registered: NAudio
/// for what it reads, ffmpeg for the rest.</para>
///
/// <para><b>Stream in, never a path.</b> Callers open stored bytes from wherever they live; the
/// ffmpeg decoder copies them to a scratch file itself, because the phone writes <c>moov</c> after
/// <c>mdat</c> and ffmpeg cannot read that from a pipe.</para>
/// </remarks>
public interface IAudioDecoder
{
    /// <summary>Whether this decoder can run on this host at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this decoder, as configured here, reads a file of this kind.</summary>
    bool CanDecode(string? contentType, string? fileName);

    /// <summary>
    /// The recording as mono samples in [-1, 1] at <see cref="AudioSourceReader.DetectionSampleRate"/>,
    /// <see cref="ChunkSamples"/> at a time.
    /// </summary>
    /// <remarks>For work that wants to report progress as it goes; the scan uses
    /// <see cref="ReadMono16kAsync"/>, which holds the result once.</remarks>
    IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct);

    /// <summary>
    /// The whole recording as one mono array at <see cref="AudioSourceReader.DetectionSampleRate"/>,
    /// allocated once from the length the file says it has.
    /// </summary>
    Task<float[]> ReadMono16kAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct);

    /// <summary>
    /// [<paramref name="startSeconds"/>, <paramref name="endSeconds"/>] as a 16-bit PCM WAV at the
    /// recording's own rate and channel count, clamped to the recording.
    /// </summary>
    Task<byte[]> DecodeRangeToWavAsync(
        Stream source, string? contentType, string? fileName,
        double startSeconds, double endSeconds, CancellationToken ct);

    /// <summary>
    /// How long the recording runs, from its header — null when the header does not say.
    /// </summary>
    Task<double?> DurationSecondsAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct);

    /// <summary>How many samples <see cref="DecodeMono16kAsync"/> hands over at a time.</summary>
    public const int ChunkSamples = 1 << 16;
}
