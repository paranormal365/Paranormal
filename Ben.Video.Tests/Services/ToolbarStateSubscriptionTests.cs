using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A component that paints the ffmpeg service's state must also subscribe to its changes.
/// </summary>
/// <remarks>
/// <para>Backlog item 94's real cause. The toolbar reads <c>Ffmpeg.State</c> straight from the
/// injected service — the status chip, its progress bar, and the Enabled of Initialize, Open,
/// Preview and Export all depend on it — but subscribed to nothing, and relied on its parent
/// re-rendering. Blazor skips a child whose parameters have not changed, and going from
/// Processing back to Ready changes none of the toolbar's parameters.</para>
///
/// <para>The result looked exactly like a hung render: a chip frozen at "Processing… 33%" with
/// Export greyed out behind it, indefinitely, while the service had returned to Ready seconds
/// earlier. Two days of the editor's own diagnostics said "Processing" because the panel was
/// reading the same stale paint. Nothing was stuck; nothing re-rendered.</para>
///
/// <para>This is a source scan rather than a render test because that is what catches the next
/// one: the failure is the *absence* of a subscription, which no test of the component's output
/// would notice while something else happens to trigger a re-render.</para>
/// </remarks>
public sealed class ToolbarStateSubscriptionTests
{
    /// <summary>Reading any of these means the component's own paint depends on ffmpeg's state.</summary>
    private static readonly string[] StateReads =
    [
        "Ffmpeg.State",
        "Ffmpeg.ProgressPercent",
        "Ffmpeg.IsWorkerBusy",
        "Ffmpeg.IsWorkerWedged",
        "Ffmpeg.DownloadLabel",
    ];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void Components_That_Paint_Ffmpeg_State_Subscribe_To_Its_Changes()
    {
        var components = Directory.EnumerateFiles(
            Path.Combine(RepoRoot().FullName, "Ben.Video.Editor", "Components"), "*.razor");

        var offenders = new List<string>();
        var checkedAny = 0;

        foreach (var file in components)
        {
            var text = File.ReadAllText(file);

            // Only components that inject the service and read its state paint from it; one that
            // receives what it needs as a parameter re-renders when that parameter changes.
            if (!text.Contains("@inject FfmpegService Ffmpeg", StringComparison.Ordinal)) continue;
            if (!StateReads.Any(r => text.Contains(r, StringComparison.Ordinal))) continue;

            checkedAny++;

            var subscribes = Regex.IsMatch(text, @"Ffmpeg\.OnStateChanged\s*\+=");
            // Either disposer counts — some of these components already implement IAsyncDisposable
            // for their JS module handles and unsubscribe there rather than adding a second
            // contract just for this.
            var unsubscribes = Regex.IsMatch(text, @"Ffmpeg\.OnStateChanged\s*-=");

            if (!subscribes)
                offenders.Add($"{Path.GetFileName(file)} — reads ffmpeg state but never subscribes to OnStateChanged");
            else if (!unsubscribes)
                offenders.Add($"{Path.GetFileName(file)} — subscribes to OnStateChanged but never unsubscribes");
        }

        Assert.True(checkedAny > 0, "No component was checked — has the editor's layout moved?");
        Assert.True(offenders.Count == 0,
            "These components paint the ffmpeg service's state without following its changes, so "
            + "they will show whatever was true at their last render:\n  "
            + string.Join("\n  ", offenders));
    }
}
