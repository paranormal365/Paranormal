using System.Diagnostics;
using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Answers "can this server read the phone's recordings?" — whether ffmpeg is configured, present
/// and runs.
/// </summary>
/// <remarks>
/// <para>Without ffmpeg an .m4a scan or clip is refused in a sentence and MP3 and WAV still work,
/// so a missing setting is quiet: nothing fails loudly, people just meet the refusal. This puts
/// the cause where an administrator will see it, on the dashboard.</para>
///
/// <para>Runs <c>ffmpeg -version</c> rather than only checking the path, because a file that
/// exists but cannot be executed (permissions, a wrong-architecture build) is the same failure to
/// the person scanning.</para>
/// </remarks>
[ApiController]
[Route("api/admin/media-tools")]
// Policy, NOT Roles — see AdminMailDiagnosticsController and AdminAuthorizationIsAPolicyTests.
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminMediaToolsController : ControllerBase
{
    private readonly MediaToolOptions _options;

    public AdminMediaToolsController(IOptions<MediaToolOptions> options) => _options = options.Value;

    [HttpGet]
    public async Task<ActionResult<MediaToolsHealth>> Get(CancellationToken ct)
    {
        var path = _options.FfmpegPath;
        if (string.IsNullOrWhiteSpace(path))
            return Ok(new MediaToolsHealth(false, null, false, null,
                "No ffmpeg: .m4a scans and clips are refused until ffmpeg.exe is in the API site's App_Data\\Tools folder (or MediaTools:FfmpegPath names it)."));

        if (!System.IO.File.Exists(path))
            return Ok(new MediaToolsHealth(true, path, false, null,
                $"MediaTools:FfmpegPath is set to {path}, but there is no file there."));

        var (version, problem) = await VersionAsync(path, ct);
        return Ok(new MediaToolsHealth(true, path, true, version, problem));
    }

    private static async Task<(string? Version, string? Problem)> VersionAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-version");

            using var process = Process.Start(psi);
            if (process is null) return (null, $"ffmpeg at {path} could not be started.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var first = (await stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (process.ExitCode != 0 || string.IsNullOrEmpty(first))
                return (null, $"ffmpeg at {path} ran but did not report a version: {(await stderr).Trim()}");

            // "ffmpeg version 6.1 Copyright (c) ..." — the copyright is noise on a dashboard.
            var copyright = first.IndexOf(" Copyright", StringComparison.Ordinal);
            return (copyright > 0 ? first[..copyright] : first, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, $"ffmpeg at {path} did not answer within 10 seconds.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return (null, $"ffmpeg at {path} could not be run: {ex.Message}");
        }
    }
}
