using System.Diagnostics;

namespace Ben.Video.Sidecar.Jobs;

public sealed record FfmpegRunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

/// <summary>
/// The only place this process ever starts another process. Every hardening measure from item
/// #38 phase E threat T5 (argv/command injection) lives here: never a shell, argv passed as a
/// list (never concatenated into a string), no stdin, a per-run working directory, and a
/// wall-clock timeout that kills the whole process tree rather than leaving an orphan.
/// </summary>
public sealed class FfmpegRunner(FfmpegLocator locator)
{
    /// <summary>
    /// Runs ffmpeg with the given argv (no leading "ffmpeg" — that's <see cref="FfmpegLocator.ExecutablePath"/>)
    /// in <paramref name="workingDirectory"/>, killing it if it exceeds <paramref name="timeout"/>.
    /// Callers are responsible for the argv's contents being safe — see
    /// <see cref="Ben.Video.Sidecar.Validation.SpecValidator"/> and <c>ArgvFactory</c> (phase 123)
    /// for how a request's typed job spec becomes this argv without ever passing through a raw
    /// string from the network.
    /// </summary>
    /// <param name="environmentOverrides">
    /// Extra environment variables for this one child process only — real ffmpeg ignores unknown
    /// vars, so this is harmless in production; its actual purpose is letting
    /// Ben.Video.Sidecar.Tests drive the fake ffmpeg binary's behavior (FAKE_FFMPEG_MODE/_OUT)
    /// per-invocation instead of through a process-wide environment variable, which would race
    /// across parallel test execution.
    /// </param>
    /// <param name="tool">
    /// Which bundled binary to run — item #70 phase 158 added ffprobe alongside ffmpeg. Defaults
    /// to <see cref="FfmpegTool.Ffmpeg"/> so every pre-existing call site is unchanged.
    /// </param>
    public async Task<FfmpegRunResult> RunAsync(
        IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout,
        Action<string>? onStdOutLine = null,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        FfmpegTool tool = FfmpegTool.Ffmpeg,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = locator.PathFor(tool),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (environmentOverrides is not null)
            foreach (var (key, value) in environmentOverrides)
                psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdOut.AppendLine(e.Data);
            onStdOutLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        // -nostdin is passed by callers, but closing our end too means ffmpeg can never block
        // waiting for interactive input even if a caller forgets the flag.
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
        }

        if (timedOut || ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        return new FfmpegRunResult(
            timedOut ? -1 : process.ExitCode, stdOut.ToString(), stdErr.ToString(), timedOut);
    }

    /// <summary>Runs <c>ffmpeg -version</c> for the health check — the cheapest possible proof
    /// the bundled binary actually executes, and the source of the version string reported by
    /// <c>GET /v1/health</c>.</summary>
    public async Task<string?> TryGetVersionAsync(CancellationToken ct = default)
    {
        if (!File.Exists(locator.ExecutablePath)) return null;
        try
        {
            var result = await RunAsync(
                ["-version"], Path.GetTempPath(), TimeSpan.FromSeconds(10), ct: ct);
            if (result.ExitCode != 0) return null;
            var firstLine = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstLine?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
