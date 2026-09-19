using Xunit;

namespace Ben.Video.Tests.Services;

/// <summary>
/// "Split at Playhead" in the clip menu must be able to be enabled (2026-09-18 audit).
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> The menu item was greyed out every time, on every clip, wherever
/// the playhead was. Splitting was reachable only by knowing the S shortcut.</para>
///
/// <para><b>Why.</b> Three lines. <c>OpenClipMenu</c> opened with <c>await SelectItem(item)</c>;
/// selecting a clip moves the playhead to that clip's start (deliberately, since the 2026-09-05
/// audit); the next line read the playhead and asked whether it was strictly greater than the
/// clip's own start. By then it was exactly equal, so the answer was no, always. The menu asked a
/// question it had just made unanswerable.</para>
///
/// <para><b>Why this is a source scan.</b> The defect is the ORDER of two statements in a Razor
/// event handler — there is no seam to call. What can be checked is that the playhead is read
/// before the selection that moves it, which is the whole of the fault.</para>
/// </remarks>
public sealed class SplitAtPlayheadIsReachableTests
{
    private static string OpenClipMenuBody()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var text = File.ReadAllText(Path.Combine(
            dir!.FullName, "Ben.Video.Editor", "Components", "VideoTimeline.razor"));

        var from = text.IndexOf("private async Task OpenClipMenu(", StringComparison.Ordinal);
        Assert.True(from >= 0, "OpenClipMenu is gone — this guard needs rewriting, not deleting.");

        var to = text.IndexOf("_clipMenuItems", from, StringComparison.Ordinal);
        Assert.True(to > from, "could not find the end of the menu's preamble");

        return text[from..to];
    }

    [Fact]
    public void The_playhead_is_read_before_the_selection_that_moves_it()
    {
        var body = OpenClipMenuBody();

        var readsPlayhead = body.IndexOf("Playback.State.TimelineTime", StringComparison.Ordinal);
        var selects       = body.IndexOf("SelectItem(item)", StringComparison.Ordinal);

        Assert.True(readsPlayhead >= 0, "OpenClipMenu no longer reads the playhead at all");
        Assert.True(selects >= 0, "OpenClipMenu no longer selects the item");
        Assert.True(readsPlayhead < selects,
            "the playhead is read after SelectItem, which has just moved it to this clip's start — " +
            "so \"Split at Playhead\" can never be enabled.");
    }

    [Fact]
    public void The_cut_is_made_where_the_playhead_was_not_where_selecting_put_it()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        var text = File.ReadAllText(Path.Combine(
            dir!.FullName, "Ben.Video.Editor", "Components", "VideoTimeline.razor"));

        var at = text.IndexOf("SplitClipAtTimelineTime(vc3.Id", StringComparison.Ordinal);
        Assert.True(at >= 0, "the menu no longer splits at the playhead");

        var call = text[at..text.IndexOf(')', at)];
        Assert.DoesNotContain("Playback.State.TimelineTime", call);
    }
}
