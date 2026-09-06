using System.Text.RegularExpressions;
using Ben.Video.Core.Services;

namespace Ben.Video.Tests.Services;

/// <summary>
/// What each keystroke means to the editor.
/// </summary>
/// <remarks>
/// The table used to be a switch inside a Razor component, where the only way to ask what a key
/// does was to press it in a browser. Every shortcut the audit found broken — Delete ignoring
/// titles and transitions, arrows never stepping frames, Cmd doing nothing on a Mac — was a
/// question this file can now answer (2026-09-05 audit, phase 11).
/// </remarks>
public sealed class EditorKeyMapTests
{
    [Theory]
    [InlineData(" ")]
    // The old IE/Edge name, still reported by some presenter remotes.
    [InlineData("Spacebar")]
    public void Space_plays_and_pauses(string key) =>
        Assert.Equal(EditorCommand.TogglePlayPause, EditorKeyMap.Resolve(key, false, false, false));

    [Theory]
    [InlineData("Delete")]
    [InlineData("Backspace")]
    public void Both_delete_keys_delete(string key) =>
        Assert.Equal(EditorCommand.DeleteSelection, EditorKeyMap.Resolve(key, false, false, false));

    [Theory]
    [InlineData("ArrowLeft",  EditorCommand.MoveLeft)]
    [InlineData("ArrowRight", EditorCommand.MoveRight)]
    [InlineData("ArrowUp",    EditorCommand.MoveUp)]
    [InlineData("ArrowDown",  EditorCommand.MoveDown)]
    public void Each_arrow_is_its_own_direction(string key, EditorCommand expected) =>
        Assert.Equal(expected, EditorKeyMap.Resolve(key, false, false, false));

    /// <summary>
    /// The arrows keep meaning the same thing with Shift held: Shift changes the nudge distance,
    /// which is the caller's business, not the table's.
    /// </summary>
    [Fact]
    public void Shift_does_not_change_what_an_arrow_means() =>
        Assert.Equal(
            EditorKeyMap.Resolve("ArrowLeft", false, false, false),
            EditorKeyMap.Resolve("ArrowLeft", false, true,  false));

    [Fact]
    public void Home_rewinds_and_End_seeks_to_the_end()
    {
        Assert.Equal(EditorCommand.Rewind,    EditorKeyMap.Resolve("Home", false, false, false));
        Assert.Equal(EditorCommand.SeekToEnd, EditorKeyMap.Resolve("End",  false, false, false));
    }

    /// <summary>
    /// Ctrl+Home and Ctrl+End are the browser's, and a text field elsewhere on the page expects
    /// to keep them.
    /// </summary>
    [Theory]
    [InlineData("Home")]
    [InlineData("End")]
    public void The_editor_leaves_the_ctrl_versions_alone(string key) =>
        Assert.Equal(EditorCommand.None, EditorKeyMap.Resolve(key, true, false, false));

    [Fact]
    public void Brackets_nudge_a_multi_selection()
    {
        Assert.Equal(EditorCommand.NudgeSelectionEarlier, EditorKeyMap.Resolve("[", false, false, false));
        Assert.Equal(EditorCommand.NudgeSelectionLater,   EditorKeyMap.Resolve("]", false, false, false));
    }

    [Theory]
    [InlineData("s")]
    [InlineData("S")]
    public void S_splits_at_the_playhead_in_either_case(string key) =>
        Assert.Equal(EditorCommand.SplitAtPlayhead, EditorKeyMap.Resolve(key, false, false, false));

    /// <summary>
    /// Ctrl+S belongs to Save. Splitting the timeline because somebody reached for Save would be
    /// destructive and unexplainable.
    /// </summary>
    [Fact]
    public void Ctrl_S_is_not_split() =>
        Assert.Equal(EditorCommand.None, EditorKeyMap.Resolve("s", true, false, false));

    [Fact]
    public void Escape_clears_the_selection() =>
        Assert.Equal(EditorCommand.ClearSelection, EditorKeyMap.Resolve("Escape", false, false, false));

    [Theory]
    [InlineData("d")]
    [InlineData("D")]
    public void Ctrl_D_duplicates(string key) =>
        Assert.Equal(EditorCommand.Duplicate, EditorKeyMap.Resolve(key, true, false, false));

    /// <summary>
    /// A bare D types a letter as far as the editor is concerned; only the chord duplicates.
    /// </summary>
    [Fact]
    public void A_bare_D_does_nothing() =>
        Assert.Equal(EditorCommand.None, EditorKeyMap.Resolve("d", false, false, false));

    [Fact]
    public void Ctrl_Z_undoes() =>
        Assert.Equal(EditorCommand.Undo, EditorKeyMap.Resolve("z", true, false, false));

    /// <summary>
    /// Both redo chords, because people arrive from different editors with different habits.
    /// </summary>
    [Theory]
    [InlineData("z", true)]
    [InlineData("Z", true)]
    [InlineData("y", false)]
    [InlineData("Y", false)]
    public void Both_redo_chords_redo(string key, bool shift) =>
        Assert.Equal(EditorCommand.Redo, EditorKeyMap.Resolve(key, true, shift, false));

    /// <summary>
    /// The modifier arrives already folded: the JS layer passes <c>ctrlKey || metaKey</c>, so Cmd+Z
    /// on a Mac reaches this table as ctrl. Forwarding only ctrlKey is why undo and redo did
    /// nothing at all on macOS, the platform this is developed on (2026-09-05 audit, timeline-8).
    /// </summary>
    [Fact]
    public void The_table_does_not_care_which_machine_the_modifier_came_from() =>
        Assert.Equal(EditorCommand.Undo, EditorKeyMap.Resolve("z", ctrl: true, shift: false, alt: false));

    [Theory]
    [InlineData("m")]
    [InlineData("M")]
    public void M_drops_a_marker(string key) =>
        Assert.Equal(EditorCommand.AddMarker, EditorKeyMap.Resolve(key, false, false, false));

    /// <summary>
    /// Only a bare M. Alt+M and Ctrl+M are menu accelerators on some platforms, and a marker
    /// appearing because somebody reached for a menu is hard to explain and easy to miss.
    /// </summary>
    [Theory]
    [InlineData(true,  false, false)]
    [InlineData(false, true,  false)]
    [InlineData(false, false, true)]
    public void A_modified_M_drops_nothing(bool ctrl, bool shift, bool alt) =>
        Assert.Equal(EditorCommand.None, EditorKeyMap.Resolve("m", ctrl, shift, alt));

    [Fact]
    public void Question_mark_toggles_the_shortcut_help() =>
        Assert.Equal(EditorCommand.ToggleHelp, EditorKeyMap.Resolve("?", false, false, false));

    [Theory]
    [InlineData("a")]
    [InlineData("F5")]
    [InlineData("Tab")]
    [InlineData("Enter")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_left_to_the_browser(string? key) =>
        Assert.Equal(EditorCommand.None, EditorKeyMap.Resolve(key, false, false, false));

    // ── The JS half of the same contract ──────────────────────────────────────

    private static string KeyboardInterop() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Video.Editor", "wwwroot", "js", "keyboardInterop.js"));

    /// <summary>
    /// Cmd counts as the modifier.
    /// </summary>
    /// <remarks>
    /// The listener forwarded <c>e.ctrlKey</c> alone, so on a Mac every Ctrl-chord in the table
    /// above — undo, redo, duplicate — was unreachable (2026-09-05 audit, timeline-8). Nothing in
    /// C# can notice that, because the C# side is handed a bool that simply never arrived true.
    /// </remarks>
    [Fact]
    public void The_listener_folds_Cmd_into_the_modifier_it_forwards()
    {
        var forward = Regex.Match(KeyboardInterop(), @"invokeMethodAsync\('OnKeyDown'[^)]*\)");

        Assert.True(forward.Success, "The keydown listener no longer forwards OnKeyDown.");
        Assert.Contains("metaKey", forward.Value);
    }

    /// <summary>
    /// A key the browser is stopped from acting on has to be a key the editor actually uses.
    /// Suppressing Space or Backspace and then doing nothing with it is worse than not claiming it.
    /// </summary>
    [Fact]
    public void Every_key_the_listener_suppresses_is_one_the_editor_handles()
    {
        // The unmodified half of the listener's claimed set; the Ctrl chords are checked above by
        // the tests for each command.
        string[] claimed =
        [
            " ", "Spacebar", "Backspace", "Delete",
            "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End",
        ];

        var js = KeyboardInterop();

        foreach (var key in claimed)
        {
            Assert.NotEqual(EditorCommand.None, EditorKeyMap.Resolve(key, false, false, false));

            // And the listener still claims it — a key dropped from the JS set would leave the
            // browser scrolling the page under a working shortcut.
            var literal = key == " " ? "e.key === ' '" : $"'{key}'";
            Assert.Contains(literal, js.Replace("e.key.startsWith('Arrow')", "'ArrowLeft' 'ArrowRight' 'ArrowUp' 'ArrowDown'"));
        }
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
