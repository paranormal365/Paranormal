using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Ben.Data.WebApi.Services;

/// <summary>Where the API can find an ffmpeg binary, if anywhere.</summary>
public sealed class MediaToolOptions
{
    /// <summary>
    /// Absolute path to an ffmpeg executable. Empty means the API cannot remux, and audio and
    /// video pass through with their metadata — the behaviour before item 181, kept as the
    /// fallback so that not configuring a tool is never a failed upload.
    /// </summary>
    public string? FfmpegPath { get; set; }

    /// <summary>How long one remux may take before it is abandoned and the original kept.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>
/// Strips embedded metadata from audio and video by remuxing through ffmpeg with
/// <c>-map_metadata -1</c> (item 181).
/// </summary>
/// <remarks>
/// <para><b>Remux, not re-encode.</b> <c>-c copy</c> keeps the streams byte-identical and only
/// rebuilds the container, so a two-hour recording costs a file copy rather than an hour of CPU —
/// and, more importantly for an investigation platform, the audio and video are not degraded. The
/// metadata lives in the container, which is exactly what gets rebuilt.</para>
///
/// <para><b>Every failure keeps the original.</b> No ffmpeg, a timeout, a non-zero exit, an empty
/// output — all return null and the caller stores what it was given. Losing evidence to a failed
/// strip would be a far worse outcome than keeping metadata that the group can still see recorded
/// in its own table.</para>
/// </remarks>
public interface IAvMetadataStripper
{
    /// <summary>Whether this host can strip A/V at all.</summary>
    bool IsAvailable { get; }

    /// <summary>True for the content types this can strip.</summary>
    bool CanStrip(string? contentType);

    /// <summary>
    /// The stripped bytes, or null when stripping was not possible — in which case the caller
    /// keeps the original.
    /// </summary>
    Task<byte[]?> StripAsync(byte[] original, string fileName, CancellationToken ct);
}

/// <inheritdoc />
public sealed class AvMetadataStripper : IAvMetadataStripper
{
    private readonly MediaToolOptions _options;
    private readonly ILogger<AvMetadataStripper> _logger;

    public AvMetadataStripper(IOptions<MediaToolOptions> options, ILogger<AvMetadataStripper> logger)
    {
        _options = options.Value;
        _logger  = logger;
    }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_options.FfmpegPath) && File.Exists(_options.FfmpegPath);

    public bool CanStrip(string? contentType)
        => contentType is not null
        && (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
         || contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase));

    public async Task<byte[]?> StripAsync(byte[] original, string fileName, CancellationToken ct)
    {
        if (!IsAvailable) return null;

        // ffmpeg works on paths, so the bytes go to a scratch file and the result comes back from
        // another. The extension is preserved because ffmpeg picks the container format from it.
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension)) return null;

        var scratch = Path.Combine(Path.GetTempPath(), $"ben-strip-{Guid.NewGuid():N}");
        var input   = scratch + ".in" + extension;
        var output  = scratch + ".out" + extension;

        try
        {
            await File.WriteAllBytesAsync(input, original, ct);

            var psi = new ProcessStartInfo(_options.FfmpegPath!)
            {
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            // -map_metadata -1 drops container metadata; -c copy keeps the streams as they are.
            // -map 0 keeps every stream (a video's audio track included) rather than ffmpeg's
            // default of one stream per type, which would silently discard the rest.
            foreach (var arg in new[] { "-y", "-i", input, "-map", "0", "-map_metadata", "-1", "-c", "copy", output })
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process is null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));

            // Drained rather than ignored: ffmpeg writes progress to stderr, and a full pipe
            // buffer deadlocks a process nobody is reading from.
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
                await Task.WhenAll(stderr, stdout);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                _logger.LogWarning("Stripping metadata from {FileName} timed out; the original is kept.", fileName);
                return null;
            }

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "ffmpeg could not strip {FileName} (exit {ExitCode}); the original is kept. {Detail}",
                    fileName, process.ExitCode, Tail(await stderr));
                return null;
            }

            if (!File.Exists(output)) return null;
            var stripped = await File.ReadAllBytesAsync(output, ct);

            // An empty or absurdly small result means the remux produced nothing usable.
            return stripped.Length > 0 ? stripped : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                     or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Stripping metadata from {FileName} failed; the original is kept.", fileName);
            return null;
        }
        finally
        {
            TryDelete(input);
            TryDelete(output);
        }
    }

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
        return string.Join(" | ", lines.TakeLast(3).Select(l => l.Trim()));
    }
}
