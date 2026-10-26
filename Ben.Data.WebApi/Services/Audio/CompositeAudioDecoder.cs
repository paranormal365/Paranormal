namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// The decoder every audio feature asks: NAudio for MP3 and WAV, ffmpeg for any other audio.
/// </summary>
/// <remarks>
/// <para>NAudio stays first for what it reads, so an MP3 or WAV decodes exactly as it always has
/// — the same samples, the same pinned EVP scores — whether or not a host has ffmpeg.</para>
///
/// <para>Without ffmpeg, anything else is refused with <see cref="UnavailableSentence"/> rather
/// than guessed at. That sentence is what reaches the person, so it says what is missing in their
/// words, and the administrator's dashboard says what to set.</para>
/// </remarks>
public sealed class CompositeAudioDecoder : IAudioDecoder
{
    /// <summary>What a person is told when this host has no ffmpeg to read the phone's recording with.</summary>
    public const string UnavailableSentence = "This server cannot read .m4a yet.";

    /// <summary>
    /// The refusal for <paramref name="contentType"/>: the phone's .m4a by name, anything else as
    /// what this server does read, so an .ogg is not told it is an .m4a.
    /// </summary>
    public static string UnavailableFor(string? contentType)
        => (contentType ?? "").ToLowerInvariant() is "audio/mp4" or "audio/x-m4a" or "audio/m4a" or "audio/aac"
            ? UnavailableSentence
            : $"This server reads WAV and MP3 recordings, and cannot read this one ('{contentType}') yet.";

    private readonly NAudioDecoder _naudio;
    private readonly FfmpegAudioDecoder _ffmpeg;

    public CompositeAudioDecoder(NAudioDecoder naudio, FfmpegAudioDecoder ffmpeg)
    {
        _naudio = naudio;
        _ffmpeg = ffmpeg;
    }

    /// <summary>NAudio is always here, so MP3 and WAV can always be read.</summary>
    public bool IsAvailable => true;

    public bool CanDecode(string? contentType, string? fileName)
        => _naudio.CanDecode(contentType, fileName) || _ffmpeg.CanDecode(contentType, fileName);

    public IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
        => For(contentType).DecodeMono16kAsync(source, contentType, fileName, ct);

    public Task<float[]> ReadMono16kAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
        => For(contentType).ReadMono16kAsync(source, contentType, fileName, ct);

    public Task<byte[]> DecodeRangeToWavAsync(
        Stream source, string? contentType, string? fileName,
        double startSeconds, double endSeconds, CancellationToken ct)
        => For(contentType).DecodeRangeToWavAsync(source, contentType, fileName, startSeconds, endSeconds, ct);

    public Task<double?> DurationSecondsAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
        => For(contentType).DurationSecondsAsync(source, contentType, fileName, ct);

    private IAudioDecoder For(string? contentType)
    {
        if (AudioSourceReader.Reads(contentType)) return _naudio;

        if (!(contentType ?? "").StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"That file isn't audio this server reads ('{contentType}').");

        if (!_ffmpeg.IsAvailable) throw new AudioDecoderUnavailableException(UnavailableFor(contentType));
        return _ffmpeg;
    }
}
