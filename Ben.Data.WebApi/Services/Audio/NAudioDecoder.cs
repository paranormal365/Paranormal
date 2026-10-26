using System.Runtime.CompilerServices;
using Ben.Data.WebApi.Controllers.Entities;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// MP3 and WAV, in-process, through <see cref="AudioSourceReader"/> — what every audio feature used
/// before ffmpeg was involved, unchanged.
/// </summary>
/// <remarks>
/// Always available: NLayer and NAudio's WAV reader are managed code, so there is nothing for a
/// host to be missing. The work is synchronous underneath; it is wrapped, not rewritten.
/// </remarks>
public sealed class NAudioDecoder : IAudioDecoder
{
    public bool IsAvailable => true;

    public bool CanDecode(string? contentType, string? fileName) => AudioSourceReader.Reads(contentType);

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(
        Stream source, string? contentType, string? fileName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var waveStream = AudioSourceReader.Open(source, contentType);
        var provider = AudioSourceReader.MonoAt(waveStream);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // A fresh array each time: the caller may keep the chunk it was handed.
            var chunk = new float[IAudioDecoder.ChunkSamples];
            var read  = provider.Read(chunk, 0, chunk.Length);
            if (read == 0) yield break;
            yield return chunk.AsMemory(0, read);
            await Task.Yield();
        }
    }

    public Task<float[]> ReadMono16kAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
        => Task.FromResult(AudioSourceReader.ReadMonoAt(source, contentType).Mono);

    public Task<byte[]> DecodeRangeToWavAsync(
        Stream source, string? contentType, string? fileName,
        double startSeconds, double endSeconds, CancellationToken ct)
        => Task.FromResult(AudioClipper.Clip(source, contentType ?? "", startSeconds, endSeconds).Bytes);

    public Task<double?> DurationSecondsAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
        => Task.FromResult<double?>(AudioSourceReader.Probe(source, contentType).Duration.TotalSeconds);
}
