using System.Diagnostics;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// What the bundled ffmpeg can actually encode with, asked once and remembered.
/// </summary>
/// <remarks>
/// <para>The exporter used to name <c>libx264</c> outright. Those encoders are the GPL part of an
/// ffmpeg build, so a build licensed for the Microsoft Store does not have them and the export
/// would fail with "Unknown encoder". Asking the binary what it has, rather than assuming, lets one
/// sidecar work with either build - see <see cref="Ben.Video.Editor.Services.VideoEncoders"/>.</para>
///
/// <para><b>A failed probe is not an error.</b> If ffmpeg cannot be run, or answers something
/// unexpected, this returns nothing and every caller falls back to the behaviour it had before any
/// of this existed. The export itself will report the real problem soon enough, and far more
/// clearly than a probe could.</para>
///
/// <para>Asked once because the binary does not change while the process runs, and the integrity
/// check at startup has already decided whether it is the one we trust.</para>
/// </remarks>
public sealed class FfmpegEncoders(FfmpegLocator locator, ILogger<FfmpegEncoders> log)
{
    private readonly Lazy<IReadOnlyCollection<string>> _names = new(() => Probe(locator, log));

    /// <summary>Encoder names this ffmpeg reports, or empty when it could not be asked.</summary>
    public IReadOnlyCollection<string> Names => _names.Value;

    private static IReadOnlyCollection<string> Probe(FfmpegLocator locator, ILogger log)
    {
        try
        {
            var psi = new ProcessStartInfo(locator.PathFor(FfmpegTool.Ffmpeg))
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");

            using var p = Process.Start(psi);
            if (p is null) return [];

            var stdout = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(TimeSpan.FromSeconds(20))) { TryKill(p); return []; }

            var names = Parse(stdout);
            log.LogInformation("ffmpeg offers {Count} encoders.", names.Count);
            return names;
        }
        catch (Exception ex)
        {
            // Deliberately swallowed: see the note above about falling back rather than failing.
            log.LogWarning(ex, "Could not ask ffmpeg which encoders it has; assuming the usual ones.");
            return [];
        }
    }

    /// <summary>
    /// Reads encoder names out of <c>ffmpeg -encoders</c>.
    /// </summary>
    /// <remarks>
    /// The listing is a header, then one encoder per line: six capability characters, a space, the
    /// name, then a description. Taking the second whitespace-separated word is enough, as long as
    /// the first field really is a capability field.
    ///
    /// <para>The header's own legend has to be kept out, and it is indented exactly like an entry.
    /// Two of its lines defeat the obvious checks: <c>.F.... = Frame-level multithreading</c> looks
    /// like flags, and <c>V..... = Video</c> passes a media-type check as well - both would add an
    /// encoder named "=". So the name itself is checked for being a name.</para>
    /// </remarks>
    public static IReadOnlyCollection<string> Parse(string listing)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in listing.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 8 || !char.IsWhiteSpace(line[0])) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            // The first field is the capability flags: a media type, then five letters or dots.
            var flags = parts[0];
            if (flags.Length != 6) continue;
            if (flags[0] is not ('V' or 'A' or 'S')) continue;
            if (!flags.Skip(1).All(c => char.IsLetter(c) || c == '.')) continue;

            var name = parts[1];
            if (!char.IsLetter(name[0])) continue;
            if (!name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.')) continue;

            names.Add(name);
        }

        return names;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { /* it is going away either way */ }
    }
}
