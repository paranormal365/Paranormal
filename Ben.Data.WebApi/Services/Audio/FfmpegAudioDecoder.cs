using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ben.Data.WebApi.Services.FieldSessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Audio;

/// <summary>
/// Anything ffmpeg reads — in practice the phone's AAC <c>.m4a</c> — through the binary at
/// <see cref="MediaToolOptions.FfmpegPath"/>.
/// </summary>
/// <remarks>
/// <para><b>Always from a scratch file.</b> The phone writes <c>moov</c> (the index) after
/// <c>mdat</c> (the audio), and ffmpeg reading <c>pipe:0</c> cannot seek back to find it. So the
/// bytes are copied to <c>{temp}/ben-evp-{guid}{ext}</c> first, the same way
/// <see cref="AvMetadataStripper"/> does for the same reason, and the copy is deleted whatever
/// happens.</para>
///
/// <para><b>Samples come back on stdout</b> as 16-bit little-endian mono at 16 kHz, which is what
/// the detector reads at; ffmpeg does the mixdown and the rate change. A range comes back as a WAV
/// FILE, not on a pipe: a WAV written to a pipe carries 0xFFFFFFFF as its length, because ffmpeg
/// cannot go back and fill it in, and NAudio's reader refuses that.</para>
///
/// <para><b>Every failure says why.</b> A non-zero exit or a timeout is an
/// <see cref="AudioDecodeFailedException"/> carrying ffmpeg's last lines, which every audio
/// endpoint already turns into a 400.</para>
/// </remarks>
public sealed class FfmpegAudioDecoder : IAudioDecoder, IAudioEncoder
{
    private const int SampleRate = AudioSourceReader.DetectionSampleRate;

    private readonly MediaToolOptions _options;

    private readonly ILogger<FfmpegAudioDecoder> _logger;

    public FfmpegAudioDecoder(IOptions<MediaToolOptions> options, ILogger<FfmpegAudioDecoder>? logger = null)
    {
        _options = options.Value;
        _logger  = logger ?? NullLogger<FfmpegAudioDecoder>.Instance;
    }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_options.FfmpegPath) && File.Exists(_options.FfmpegPath);

    public bool CanDecode(string? contentType, string? fileName)
        => IsAvailable
        && ((contentType ?? "").StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
            || ExtensionFor(contentType, fileName) is ".m4a" or ".mp4" or ".aac" or ".ogg" or ".opus"
                                                     or ".flac" or ".caf" or ".mov");

    public async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeMono16kAsync(
        Stream source, string? contentType, string? fileName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureAvailable();
        var input = await CopyToScratchAsync(source, contentType, fileName, ct);
        try
        {
            await foreach (var chunk in DecodeFileAsync(input, ct))
                yield return chunk;
        }
        finally
        {
            TryDelete(input);
        }
    }

    public async Task<float[]> ReadMono16kAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
    {
        EnsureAvailable();
        var input = await CopyToScratchAsync(source, contentType, fileName, ct);
        try
        {
            // Sized once from the header when it says how long the recording is, the same rule
            // AudioSourceReader keeps: growing a gigabyte-scale buffer is how scans ran out of memory.
            double? seconds;
            await using (var probe = File.OpenRead(input))
                seconds = MediaDuration.Seconds(probe);

            var buffer = new float[(int)Math.Clamp(((seconds ?? 0) + 1) * SampleRate, 1 << 16, 0x7FEFFFFF)];
            var count  = 0;
            await foreach (var chunk in DecodeFileAsync(input, ct))
            {
                if (count + chunk.Length > buffer.Length)
                    Array.Resize(ref buffer, (int)Math.Min(0x7FEFFFFFL,
                        Math.Max(count + chunk.Length, buffer.Length + buffer.Length / 2L)));
                chunk.Span.CopyTo(buffer.AsSpan(count));
                count += chunk.Length;
            }
            if (count != buffer.Length) Array.Resize(ref buffer, count);
            return buffer;
        }
        finally
        {
            TryDelete(input);
        }
    }

    public async Task<byte[]> DecodeRangeToWavAsync(
        Stream source, string? contentType, string? fileName,
        double startSeconds, double endSeconds, CancellationToken ct)
    {
        EnsureAvailable();
        var input  = await CopyToScratchAsync(source, contentType, fileName, ct);
        var output = Path.ChangeExtension(input, ".out.wav");
        try
        {
            var start    = Math.Max(0, startSeconds);
            var duration = Math.Max(0, endSeconds - start);
            using var process = Start(
                "-ss", Seconds(start), "-t", Seconds(duration), "-i", input,
                "-vn", "-map", "0:a:0", "-c:a", "pcm_s16le", "-y", output);

            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await WaitAsync(process, stderr, ct);

            return File.Exists(output) ? await File.ReadAllBytesAsync(output, ct) : [];
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

    public async Task<double?> DurationSecondsAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
    {
        if (source.CanSeek) return MediaDuration.Seconds(source);

        EnsureAvailable();
        var input = await CopyToScratchAsync(source, contentType, fileName, ct);
        try
        {
            await using var copy = File.OpenRead(input);
            return MediaDuration.Seconds(copy);
        }
        finally
        {
            TryDelete(input);
        }
    }

    public bool CanEncodeFlac => IsAvailable;

    public async Task<byte[]?> TryWavToFlacAsync(byte[] wav, CancellationToken ct)
    {
        if (!IsAvailable) return null;

        var input  = Path.Combine(Path.GetTempPath(), $"ben-flac-{Guid.NewGuid():N}.wav");
        var output = Path.ChangeExtension(input, ".flac");
        try
        {
            await File.WriteAllBytesAsync(input, wav, ct);
            // Level 8 is FLAC's smallest; it costs time to write, never to play, and never loses anything.
            using var process = Start("-i", input, "-c:a", "flac", "-compression_level", "8", "-y", output);

            var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await WaitAsync(process, stderr, ct);

            return File.Exists(output) ? await File.ReadAllBytesAsync(output, ct) : null;
        }
        catch (Exception ex) when (ex is AudioDecodeFailedException or AudioDecoderUnavailableException
                                       or IOException or System.ComponentModel.Win32Exception)
        {
            // Saved as WAV instead, which loses nothing either; the page says which it got.
            _logger.LogWarning(ex, "FLAC encoding failed; saving WAV instead");
            return null;
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

    // ── ffmpeg ────────────────────────────────────────────────────────────────

    private async IAsyncEnumerable<ReadOnlyMemory<float>> DecodeFileAsync(
        string input, [EnumeratorCancellation] CancellationToken ct)
    {
        using var process = Start(
            "-i", input, "-vn", "-map", "0:a:0", "-f", "s16le", "-ac", "1", "-ar", $"{SampleRate}", "pipe:1");

        // Drained rather than ignored: a full stderr pipe deadlocks a process nobody reads from.
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.DecodeTimeoutSeconds)));

        var stdout = process.StandardOutput.BaseStream;
        var bytes  = new byte[IAudioDecoder.ChunkSamples * 2];
        var filled = 0;
        try
        {
            while (true)
            {
                var read = await ReadOrGiveUpAsync(process, stdout, bytes.AsMemory(filled), timeout, ct);
                if (read == 0) break;
                filled += read;
                if (filled < bytes.Length) continue;

                yield return ToFloats(bytes, filled);
                filled = 0;
            }

            // An odd trailing byte is half a sample ffmpeg never finished; it is dropped.
            if (filled >= 2) yield return ToFloats(bytes, filled & ~1);

            await WaitAsync(process, stderr, timeout.Token);
        }
        finally
        {
            // A caller that stops early (or a timeout) must not leave ffmpeg running.
            TryKill(process);
        }
    }

    private async Task<int> ReadOrGiveUpAsync(
        Process process, Stream stdout, Memory<byte> into, CancellationTokenSource timeout, CancellationToken ct)
    {
        try
        {
            return await stdout.ReadAsync(into, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new AudioDecodeFailedException(
                $"Reading that recording took longer than {_options.DecodeTimeoutSeconds} seconds, so it was stopped.");
        }
    }

    private async Task WaitAsync(Process process, Task<string> stderr, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.DecodeTimeoutSeconds)));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new AudioDecodeFailedException(
                $"Reading that recording took longer than {_options.DecodeTimeoutSeconds} seconds, so it was stopped.");
        }

        if (process.ExitCode != 0)
            throw new AudioDecodeFailedException($"ffmpeg could not read it: {Tail(await stderr)}");
    }

    private Process Start(params string[] args)
    {
        var psi = new ProcessStartInfo(_options.FfmpegPath!)
        {
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error" }.Concat(args))
            psi.ArgumentList.Add(arg);

        return Process.Start(psi)
            ?? throw new AudioDecoderUnavailableException("This server could not start ffmpeg to read that recording.");
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable) throw new AudioDecoderUnavailableException(CompositeAudioDecoder.UnavailableSentence);
    }

    private static async Task<string> CopyToScratchAsync(
        Stream source, string? contentType, string? fileName, CancellationToken ct)
    {
        if (source.CanSeek) source.Position = 0;
        var path = Path.Combine(Path.GetTempPath(), $"ben-evp-{Guid.NewGuid():N}{ExtensionFor(contentType, fileName)}");
        try
        {
            await using var copy = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                                                  FileShare.None, bufferSize: 81920, useAsync: true);
            await source.CopyToAsync(copy, ct);
            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    /// <summary>The file's own extension, or one from its content type; ffmpeg probes either way.</summary>
    private static string ExtensionFor(string? contentType, string? fileName)
    {
        var fromName = Path.GetExtension(fileName ?? "");
        if (!string.IsNullOrWhiteSpace(fromName)) return fromName.ToLowerInvariant();

        return (contentType ?? "").ToLowerInvariant() switch
        {
            "audio/mp4" or "audio/x-m4a" or "audio/m4a" => ".m4a",
            "audio/aac"                                  => ".aac",
            "audio/ogg"                                  => ".ogg",
            "audio/opus"                                 => ".opus",
            "audio/flac" or "audio/x-flac"               => ".flac",
            "audio/x-caf"                                => ".caf",
            _                                            => ".bin",
        };
    }

    private static ReadOnlyMemory<float> ToFloats(byte[] bytes, int length)
    {
        var samples = new float[length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / 32768f;
        return samples;
    }

    private static string Seconds(double value)
        => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>ffmpeg's last few lines — the part that says why, without the banner.</summary>
    private static string Tail(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length == 0
            ? "it gave no reason."
            : string.Join(" | ", lines.TakeLast(3).Select(l => l.Trim()));
    }
}
