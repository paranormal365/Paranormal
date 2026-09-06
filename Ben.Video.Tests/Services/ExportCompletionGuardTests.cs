using System.Text.RegularExpressions;

namespace Ben.Video.Tests.Services;

/// <summary>
/// A render that was stopped is not a render that finished.
/// </summary>
/// <remarks>
/// <para>The export dialog awaited the render and then told the editor it was complete, whatever
/// the job's final state. So pressing Cancel answered with a window titled "Export Complete" and a
/// prompt asking whether to save the project — the editor reporting success for the thing somebody
/// had just stopped. Found during the large-media walk (phase 12).</para>
///
/// <para>A source scan rather than a unit test, because the mistake is in a Razor component: the
/// callback is invoked from markup that no plain class can be handed. What can be checked is that
/// the invocation is still guarded by the job's state.</para>
/// </remarks>
public sealed class ExportCompletionGuardTests
{
    private static string EditorFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "Ben.Video.Editor", .. parts]));

    /// <summary>
    /// The dialog only reports completion for a render that completed.
    /// </summary>
    [Fact]
    public void A_cancelled_render_is_not_reported_as_a_finished_one()
    {
        var source = EditorFile("Components", "ExportDialog.razor");

        var start = source.IndexOf("private async Task StartExportAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0, "StartExportAsync has been renamed; this guard needs updating.");

        // The immediate-export path only, which is the one that awaits a render. The queue path
        // below it fires the same callback at enqueue time, and that is by design.
        var body = source[start..source.IndexOf("private async Task AddToQueueAsync()", start, StringComparison.Ordinal)];

        Assert.Contains("OnExportComplete.InvokeAsync", body);
        Assert.True(
            Regex.IsMatch(body, @"State\s+is\s+ExportJobState\.Completed"),
            "StartExportAsync reports completion without checking that the job completed, so "
            + "cancelling an export answers with \"Export Complete\".");
    }

    /// <summary>
    /// And the editor refuses the report as well, from the other side.
    /// </summary>
    [Fact]
    public void The_editor_ignores_a_completion_report_for_a_stopped_render()
    {
        var source = EditorFile("Components", "VideoEditor.razor");

        var start = source.IndexOf("private void OnExportComplete()", StringComparison.Ordinal);
        Assert.True(start >= 0, "OnExportComplete has been renamed; this guard needs updating.");

        var body = source.Substring(start, Math.Min(1200, source.Length - start));

        Assert.True(
            body.Contains("ExportJobState.Cancelled") && body.Contains("ExportJobState.Failed"),
            "OnExportComplete acts on a cancelled or failed render as though it had finished.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
